using Humans.Application.Interfaces;
using Humans.Application.Interfaces.EarlyEntry;
using Humans.Application.Interfaces.Gate;
using Humans.Application.Interfaces.Repositories;
using Humans.Application.Interfaces.Shifts;
using Humans.Application.Interfaces.Tickets;
using Humans.Domain.Entities;
using Humans.Domain.Enums;
using NodaTime;

namespace Humans.Application.Services.Gate;

/// <summary>
/// The Gate (admissions) section service. Evaluates scans against
/// <see cref="GateAdmissionRules"/> using cross-section reads (Tickets,
/// EarlyEntry, BurnSettings), records the agent's decision as an append-only
/// <c>gate_scan_events</c> row through <see cref="IGateRepository"/>, and reads
/// back leaderboard/settings. The cutoff is always evaluated against the server
/// clock (<see cref="IClock"/>), never a device clock.
/// </summary>
public sealed class GateService(
    IGateRepository repository,
    ITicketServiceRead tickets,
    IEarlyEntryService earlyEntry,
    IBurnSettingsService burnSettings,
    IClock clock) : IGateService
{
    public async Task<GateScanResult> EvaluateAsync(string barcode, CancellationToken ct = default)
    {
        var code = GateBarcode.Normalize(barcode);
        if (code.Length == 0)
            return NotFound(code);

        var orders = await tickets.GetTicketOrdersAsync(ct);
        var attendee = orders
            .Where(o => o.IsCurrentEvent)
            .SelectMany(o => o.Attendees)
            .FirstOrDefault(a => a.Barcode is not null
                && string.Equals(GateBarcode.Normalize(a.Barcode), code, StringComparison.Ordinal));

        if (attendee is null)
            return NotFound(code);

        var settings = await repository.GetSettingsAsync(ct);
        var priorAdmit = await repository.GetAdmitForBarcodeAsync(code, ct);
        var burn = await burnSettings.GetActiveAsync(ct);
        var now = clock.GetCurrentInstant();

        UserEarlyEntry? ee = attendee.MatchedUserId is { } uid
            ? await earlyEntry.GetForUserAsync(uid, ct)
            : null;

        var outcome = GateAdmissionRules.Evaluate(new GateScanContext(
            Found: true,
            IsVoid: attendee.Status == TicketAttendeeStatus.Void,
            AlreadyAdmittedLocally: priorAdmit is not null,
            CheckedInAtVendor: attendee.Status == TicketAttendeeStatus.CheckedIn,
            Now: now,
            GeneralEntryOpensAt: settings.GeneralEntryOpensAt,
            MatchedToHuman: attendee.MatchedUserId is not null,
            EarliestEntryDate: ee?.EarliestEntryDate,
            Today: TodayInEventZone(now, burn?.TimeZoneId)));

        return new GateScanResult(
            outcome,
            code,
            attendee.AttendeeName,
            attendee.TicketTypeName,
            IsEarly: outcome == GatePreCheckOutcome.NeedsIdCheckEarly,
            EarlyEntrySource: ee is { Sources.Count: > 0 } ? string.Join(", ", ee.Sources) : null,
            TicketAttendeeId: attendee.Id,
            GuestUserId: attendee.MatchedUserId,
            PreviousAdmitAt: priorAdmit?.OccurredAt,
            PreviousAdmitByUserId: priorAdmit?.ScannedByUserId);
    }

    public async Task<GateDecisionResult> RecordDecisionAsync(
        GateDecisionInput input, Guid scannedByUserId, CancellationToken ct = default)
    {
        // Re-evaluate authoritatively: a client-supplied "ID confirmed" can never
        // turn a STOP into an admit.
        var eval = await EvaluateAsync(input.Barcode, ct);
        var verdict = ResolveVerdict(eval.Outcome, input);
        var admit = IsAdmit(verdict);

        var recorded = await repository.RecordScanAsync(BuildEvent(eval, input, scannedByUserId, verdict, admit), ct);

        if (recorded == GateRecordOutcome.DuplicateAdmitRejected)
        {
            // Lost the concurrent race for this barcode's single admit slot:
            // record the attempt as a duplicate and report it as such.
            verdict = GateVerdict.RejectedDuplicate;
            await repository.RecordScanAsync(BuildEvent(eval, input, scannedByUserId, verdict, admit: false), ct);
            var prior = await repository.GetAdmitForBarcodeAsync(eval.Barcode, ct);
            return new GateDecisionResult(verdict, eval.GuestName, eval.TicketTypeName,
                IsEarly: false, prior?.OccurredAt, prior?.ScannedByUserId);
        }

        return new GateDecisionResult(verdict, eval.GuestName, eval.TicketTypeName,
            IsEarly: admit && eval.IsEarly,
            eval.PreviousAdmitAt, eval.PreviousAdmitByUserId);
    }

    public async Task<GateLeaderboard> GetLeaderboardAsync(Instant since, CancellationToken ct = default)
    {
        var scans = await repository.GetScansSinceAsync(since, ct);
        var rows = scans
            .GroupBy(s => s.ScannedByUserId)
            .Select(g =>
            {
                var admitted = g.Count(s => IsAdmit(s.Verdict));
                return new GateLeaderboardRow(g.Key, admitted, g.Count() - admitted, g.Count());
            })
            .ToList();
        return new GateLeaderboard(rows.Sum(r => r.Admitted), scans.Count, rows);
    }

    public async Task<GateSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await repository.GetSettingsAsync(ct);
        return new GateSettingsDto(s.GeneralEntryOpensAt, s.MinorAgeThresholdYears);
    }

    public Task SaveSettingsAsync(GateSettingsDto settings, CancellationToken ct = default) =>
        repository.SaveSettingsAsync(
            new GateSettings
            {
                Id = 1,
                GeneralEntryOpensAt = settings.GeneralEntryOpensAt,
                MinorAgeThresholdYears = settings.MinorAgeThresholdYears,
            },
            ct);

    private static GateScanResult NotFound(string code) =>
        new(GatePreCheckOutcome.Invalid, code, null, null, false, null, null, null, null, null);

    private static GateVerdict ResolveVerdict(GatePreCheckOutcome outcome, GateDecisionInput input) => outcome switch
    {
        GatePreCheckOutcome.Invalid => GateVerdict.RejectedInvalid,
        GatePreCheckOutcome.Duplicate => GateVerdict.RejectedDuplicate,
        GatePreCheckOutcome.TooEarly => GateVerdict.RejectedTooEarly,
        GatePreCheckOutcome.EarlyEntryUnknown => GateVerdict.Unresolved,
        GatePreCheckOutcome.NeedsIdCheck or GatePreCheckOutcome.NeedsIdCheckEarly =>
            input.ChildWithAdult ? GateVerdict.AdmittedChildWithAdult
            : input.IdConfirmed
                ? (outcome == GatePreCheckOutcome.NeedsIdCheckEarly ? GateVerdict.AdmittedEarly : GateVerdict.Admitted)
                : GateVerdict.RejectedNameMismatch,
        _ => GateVerdict.Unresolved,
    };

    private GateScanEvent BuildEvent(
        GateScanResult eval, GateDecisionInput input, Guid scannedByUserId, GateVerdict verdict, bool admit) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = clock.GetCurrentInstant(),
            ScannedByUserId = scannedByUserId,
            Barcode = eval.Barcode,
            TicketAttendeeId = eval.TicketAttendeeId,
            GuestUserId = eval.GuestUserId,
            Verdict = verdict,
            LaneId = input.LaneId,
            ClientScanAt = input.ClientScanAt,
            Note = input.Note,
            AdmitDedupeKey = admit ? eval.Barcode : null,
        };

    private static bool IsAdmit(GateVerdict v) =>
        v is GateVerdict.Admitted or GateVerdict.AdmittedEarly or GateVerdict.AdmittedChildWithAdult;

    private static LocalDate TodayInEventZone(Instant now, string? timeZoneId)
    {
        var zone = (timeZoneId is not null ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZoneId) : null)
                   ?? DateTimeZone.Utc;
        return now.InZone(zone).Date;
    }
}
