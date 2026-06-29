using System.Security.Cryptography;
using System.Text;
using Hangfire;
using Humans.Application.Interfaces.Gate;
using Humans.Application.Interfaces.Users;
using Humans.Infrastructure.Jobs;
using Humans.Web.Authorization;
using Humans.Web.Models.Gate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;

namespace Humans.Web.Controllers;

/// <summary>
/// Gate admissions terminal. Distinct from the read-only <c>Scanner</c> section:
/// this one decides entry and writes the durable <c>gate_scan_events</c> record.
/// The terminal authenticates via the <see cref="PolicyNames.ScannerAccess"/>
/// policy (shared gate-terminal account or a ticket admin); the individual
/// staffer "claims" the session so each scan is attributed to a real Human —
/// the attribution id is read from the session, never the request body.
/// </summary>
[Authorize(Policy = PolicyNames.ScannerAccess)]
[Route("Gate")]
public sealed class GateController(
    IGateService gate,
    IUserServiceRead users,
    IConfiguration configuration,
    IClock clock) : HumansControllerBase(users)
{
    private const string ScannerSessionKey = "GateScannerId";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (GetActiveScannerId() is not { } scanner)
            return RedirectToAction(nameof(Claim));

        var info = await UserService.GetUserInfoAsync(scanner, ct);
        var asOf = InstantPattern.ExtendedIso.Format(clock.GetCurrentInstant());
        return View(new GateIndexViewModel(info?.BurnerName ?? "Gate staff", DataStale: false, asOf));
    }

    [HttpGet("Evaluate")]
    public async Task<IActionResult> Evaluate(string barcode, CancellationToken ct)
    {
        if (GetActiveScannerId() is null)
            return Unauthorized();

        var result = await gate.EvaluateAsync(barcode, ct);
        return PartialView("_VerdictCard", GateScanCardViewModel.FromEvaluation(result));
    }

    [HttpPost("Decision")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decision(
        string barcode, bool idConfirmed, bool childWithAdult,
        string? supervisorPin, string? laneId, CancellationToken ct)
    {
        if (GetActiveScannerId() is not { } scanner)
            return Unauthorized();

        // The ID waiver (child with adult) needs a server-verified supervisor PIN.
        if (childWithAdult && !SupervisorPinValid(supervisorPin))
            return PartialView("_VerdictCard", new GateScanCardViewModel(
                GateCardKind.Amber, "Supervisor", null,
                "Supervisor PIN required to admit a child without ID", false, barcode, false));

        var decision = await gate.RecordDecisionAsync(
            new GateDecisionInput(barcode, idConfirmed, childWithAdult, laneId, ClientScanAt: null, Note: null),
            scanner, ct);

        // Best-effort vendor check-in mirror on admit — fire-and-forget so the gate never waits.
        if (decision.VendorTicketId is { Length: > 0 } vendorTicketId)
            BackgroundJob.Enqueue<GateVendorCheckInJob>(j => j.ExecuteAsync(vendorTicketId, CancellationToken.None));

        return PartialView("_VerdictCard", GateScanCardViewModel.FromDecision(decision, barcode));
    }

    [HttpGet("Claim")]
    public IActionResult Claim() => View();

    [HttpPost("Claim")]
    [ValidateAntiForgeryToken]
    public IActionResult Claim(Guid userId)
    {
        HttpContext.Session.SetString(ScannerSessionKey, userId.ToString());
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Leaderboard")]
    public async Task<IActionResult> Leaderboard(CancellationToken ct)
    {
        var since = clock.GetCurrentInstant().Minus(Duration.FromDays(7));
        var board = await gate.GetLeaderboardAsync(since, ct);

        var rows = board.Rows
            .OrderByDescending(r => r.Admitted)
            .ThenByDescending(r => r.Total)
            .Select(r => new GateLeaderboardRowViewModel(r.ScannedByUserId, r.Admitted, r.Rejected, r.Total))
            .ToList();

        ViewData["TotalAdmitted"] = board.TotalAdmitted;
        ViewData["TotalScanned"] = board.TotalScanned;
        return View(rows);
    }

    [HttpGet("Admin")]
    [Authorize(Policy = PolicyNames.TicketAdminOrAdmin)]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        var s = await gate.GetSettingsAsync(ct);
        return View(new GateSettingsViewModel(
            InstantPattern.ExtendedIso.Format(s.GeneralEntryOpensAt), s.MinorAgeThresholdYears));
    }

    [HttpPost("Admin")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PolicyNames.TicketAdminOrAdmin)]
    public async Task<IActionResult> Admin(GateSettingsViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        var parsed = InstantPattern.ExtendedIso.Parse(model.GeneralEntryOpensAtUtc ?? string.Empty);
        if (!parsed.Success)
        {
            SetError("Invalid date/time — use an ISO instant, e.g. 2026-07-06T10:00:00Z.");
            return View(model);
        }

        await gate.SaveSettingsAsync(new GateSettingsDto(parsed.Value, model.MinorAgeThresholdYears), ct);
        SetSuccess("Gate settings saved.");
        return RedirectToAction(nameof(Admin));
    }

    private Guid? GetActiveScannerId() =>
        Guid.TryParse(HttpContext.Session.GetString(ScannerSessionKey), out var id) ? id : null;

    private bool SupervisorPinValid(string? pin)
    {
        var configured = configuration["Gate:SupervisorPin"];
        if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(pin))
            return false;

        // Compare fixed-width hashes so neither equality nor PIN length leaks via timing.
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
