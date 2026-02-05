using System.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Profiles.Application.DTOs;
using Profiles.Application.Interfaces;
using Profiles.Domain.Entities;
using Profiles.Infrastructure.Data;
using Profiles.Web.Models;

namespace Profiles.Web.Controllers;

[Authorize]
public class ProfileController : Controller
{
    private readonly ProfilesDbContext _dbContext;
    private readonly UserManager<User> _userManager;
    private readonly IClock _clock;
    private readonly ILogger<ProfileController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IContactFieldService _contactFieldService;
    private readonly IEmailService _emailService;

    private const string EmailVerificationTokenPurpose = "PreferredEmailVerification";
    private const int VerificationCooldownMinutes = 5;

    public ProfileController(
        ProfilesDbContext dbContext,
        UserManager<User> userManager,
        IClock clock,
        ILogger<ProfileController> logger,
        IConfiguration configuration,
        IContactFieldService contactFieldService,
        IEmailService emailService)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _clock = clock;
        _logger = logger;
        _configuration = configuration;
        _contactFieldService = contactFieldService;
        _emailService = emailService;
    }

    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        var profile = await _dbContext.Profiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        // Get consent status
        var requiredVersions = await _dbContext.DocumentVersions
            .Where(v => v.RequiresReConsent || v.LegalDocument.Versions
                .OrderByDescending(dv => dv.EffectiveFrom)
                .First().Id == v.Id)
            .Select(v => v.Id)
            .ToListAsync();

        var userConsents = await _dbContext.ConsentRecords
            .Where(c => c.UserId == user.Id)
            .Select(c => c.DocumentVersionId)
            .ToListAsync();

        var pendingConsents = requiredVersions.Except(userConsents).Count();

        // Get contact fields (user viewing their own profile sees all)
        var contactFields = profile != null
            ? await _contactFieldService.GetVisibleContactFieldsAsync(profile.Id, user.Id)
            : [];

        var viewModel = new ProfileViewModel
        {
            Id = profile?.Id ?? Guid.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            BurnerName = profile?.BurnerName ?? string.Empty,
            FirstName = profile?.FirstName ?? string.Empty,
            LastName = profile?.LastName ?? string.Empty,
            PhoneCountryCode = profile?.PhoneCountryCode,
            PhoneNumber = profile?.PhoneNumber,
            City = profile?.City,
            CountryCode = profile?.CountryCode,
            Bio = profile?.Bio,
            HasPendingConsents = pendingConsents > 0,
            PendingConsentCount = pendingConsents,
            MembershipStatus = profile != null ? ComputeStatus(profile, user.Id).ToString() : "Incomplete",
            CanViewLegalName = true, // User viewing their own profile
            ContactFields = contactFields.Select(cf => new ContactFieldViewModel
            {
                Id = cf.Id,
                FieldType = cf.FieldType,
                Label = cf.Label,
                Value = cf.Value,
                Visibility = cf.Visibility
            }).ToList()
        };

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        var profile = await _dbContext.Profiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        // Get all contact fields for editing
        var contactFields = profile != null
            ? await _contactFieldService.GetAllContactFieldsAsync(profile.Id)
            : [];

        var viewModel = new ProfileViewModel
        {
            Id = profile?.Id ?? Guid.Empty,
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            BurnerName = profile?.BurnerName ?? string.Empty,
            FirstName = profile?.FirstName ?? string.Empty,
            LastName = profile?.LastName ?? string.Empty,
            PhoneCountryCode = profile?.PhoneCountryCode,
            PhoneNumber = profile?.PhoneNumber,
            City = profile?.City,
            CountryCode = profile?.CountryCode,
            Latitude = profile?.Latitude,
            Longitude = profile?.Longitude,
            PlaceId = profile?.PlaceId,
            Bio = profile?.Bio,
            CanViewLegalName = true, // User editing their own profile
            EditableContactFields = contactFields.Select(cf => new ContactFieldEditViewModel
            {
                Id = cf.Id,
                FieldType = cf.FieldType,
                CustomLabel = cf.CustomLabel,
                Value = cf.Value,
                Visibility = cf.Visibility,
                DisplayOrder = cf.DisplayOrder
            }).ToList()
        };

        ViewData["GoogleMapsApiKey"] = _configuration["GoogleMaps:ApiKey"];
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ProfileViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["GoogleMapsApiKey"] = _configuration["GoogleMaps:ApiKey"];
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        var profile = await _dbContext.Profiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        var now = _clock.GetCurrentInstant();

        if (profile == null)
        {
            profile = new Profile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.Profiles.Add(profile);
            await _dbContext.SaveChangesAsync(); // Save to get the profile ID for contact fields
        }

        profile.BurnerName = model.BurnerName;
        profile.FirstName = model.FirstName;
        profile.LastName = model.LastName;
        profile.PhoneCountryCode = model.PhoneCountryCode;
        profile.PhoneNumber = model.PhoneNumber;
        profile.City = model.City;
        profile.CountryCode = model.CountryCode;
        profile.Latitude = model.Latitude;
        profile.Longitude = model.Longitude;
        profile.PlaceId = model.PlaceId;
        profile.Bio = model.Bio;
        profile.UpdatedAt = now;

        // Update display name on user to burner name (public-facing name)
        user.DisplayName = model.BurnerName;
        await _userManager.UpdateAsync(user);

        await _dbContext.SaveChangesAsync();

        // Save contact fields
        var contactFieldDtos = model.EditableContactFields
            .Where(cf => !string.IsNullOrWhiteSpace(cf.Value))
            .Select((cf, index) => new ContactFieldEditDto(
                cf.Id,
                cf.FieldType,
                cf.CustomLabel,
                cf.Value,
                cf.Visibility,
                index
            ))
            .ToList();

        await _contactFieldService.SaveContactFieldsAsync(profile.Id, contactFieldDtos);

        _logger.LogInformation("User {UserId} updated their profile", user.Id);

        TempData["SuccessMessage"] = "Profile updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    private string ComputeStatus(Profile profile, Guid userId)
    {
        if (profile.IsSuspended)
        {
            return "Suspended";
        }

        // Simplified status check - full implementation would use IMembershipCalculator
        return "Active";
    }

    [HttpGet]
    public async Task<IActionResult> PreferredEmail()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        var viewModel = BuildPreferredEmailViewModel(user);
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPreferredEmail(PreferredEmailViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(model.NewEmail))
        {
            ModelState.AddModelError(nameof(model.NewEmail), "Please enter an email address.");
            return View(nameof(PreferredEmail), BuildPreferredEmailViewModel(user));
        }

        var newEmail = model.NewEmail.Trim();

        // Check if same as OAuth email
        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.NewEmail),
                "This is already your sign-in email. No need to set it as preferred.");
            return View(nameof(PreferredEmail), BuildPreferredEmailViewModel(user));
        }

        // Check rate limit (5 minute cooldown)
        var now = _clock.GetCurrentInstant();
        if (user.PreferredEmailVerificationSentAt.HasValue)
        {
            var cooldownEnd = user.PreferredEmailVerificationSentAt.Value.Plus(Duration.FromMinutes(VerificationCooldownMinutes));
            if (now < cooldownEnd)
            {
                var minutesRemaining = (int)Math.Ceiling((cooldownEnd - now).TotalMinutes);
                ModelState.AddModelError(nameof(model.NewEmail),
                    $"Please wait {minutesRemaining} minute(s) before requesting another verification email.");
                return View(nameof(PreferredEmail), BuildPreferredEmailViewModel(user));
            }
        }

        // Check uniqueness among verified preferred emails (case-insensitive)
        var emailInUse = await _dbContext.Users
            .AnyAsync(u => u.Id != user.Id
                && u.PreferredEmailVerified
                && u.PreferredEmail != null
                && EF.Functions.ILike(u.PreferredEmail, newEmail));

        if (emailInUse)
        {
            ModelState.AddModelError(nameof(model.NewEmail),
                "This email address is already in use by another account.");
            return View(nameof(PreferredEmail), BuildPreferredEmailViewModel(user));
        }

        // Generate verification token
        var token = await _userManager.GenerateUserTokenAsync(
            user,
            TokenOptions.DefaultEmailProvider,
            EmailVerificationTokenPurpose);

        // Update user with pending email
        user.PreferredEmail = newEmail;
        user.PreferredEmailVerified = false;
        user.PreferredEmailVerificationSentAt = now;
        await _userManager.UpdateAsync(user);

        // Build verification URL
        var verificationUrl = Url.Action(
            nameof(VerifyEmail),
            "Profile",
            new { userId = user.Id, token = HttpUtility.UrlEncode(token) },
            Request.Scheme);

        // Send verification email
        await _emailService.SendEmailVerificationAsync(
            newEmail,
            user.DisplayName,
            verificationUrl!);

        _logger.LogInformation(
            "Sent preferred email verification to {Email} for user {UserId}",
            newEmail, user.Id);

        TempData["SuccessMessage"] = $"Verification email sent to {newEmail}. Please check your inbox.";
        return RedirectToAction(nameof(PreferredEmail));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(Guid userId, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            TempData["ErrorMessage"] = "Invalid verification link.";
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            TempData["ErrorMessage"] = "Invalid verification link.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrEmpty(user.PreferredEmail))
        {
            TempData["ErrorMessage"] = "No email pending verification.";
            return RedirectToAction(nameof(PreferredEmail));
        }

        // Verify the token
        var decodedToken = HttpUtility.UrlDecode(token);
        var isValid = await _userManager.VerifyUserTokenAsync(
            user,
            TokenOptions.DefaultEmailProvider,
            EmailVerificationTokenPurpose,
            decodedToken);

        if (!isValid)
        {
            TempData["ErrorMessage"] = "The verification link is invalid or has expired.";
            return RedirectToAction(nameof(PreferredEmail));
        }

        // Re-check uniqueness (guard against race conditions, case-insensitive)
        var emailInUse = await _dbContext.Users
            .AnyAsync(u => u.Id != user.Id
                && u.PreferredEmailVerified
                && u.PreferredEmail != null
                && EF.Functions.ILike(u.PreferredEmail, user.PreferredEmail));

        if (emailInUse)
        {
            TempData["ErrorMessage"] = "This email address has been claimed by another account.";
            user.PreferredEmail = null;
            user.PreferredEmailVerified = false;
            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(PreferredEmail));
        }

        // Mark as verified
        user.PreferredEmailVerified = true;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation(
            "User {UserId} verified preferred email {Email}",
            user.Id, user.PreferredEmail);

        TempData["SuccessMessage"] = $"Email address {user.PreferredEmail} has been verified.";
        return RedirectToAction(nameof(PreferredEmail));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearPreferredEmail()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        var previousEmail = user.PreferredEmail;
        user.PreferredEmail = null;
        user.PreferredEmailVerified = false;
        user.PreferredEmailVerificationSentAt = null;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation(
            "User {UserId} cleared preferred email (was: {Email})",
            user.Id, previousEmail);

        TempData["SuccessMessage"] = "Preferred email has been removed. System emails will now be sent to your sign-in email.";
        return RedirectToAction(nameof(PreferredEmail));
    }

    private PreferredEmailViewModel BuildPreferredEmailViewModel(User user)
    {
        var now = _clock.GetCurrentInstant();
        var canResend = true;
        var minutesUntilResend = 0;

        if (user.PreferredEmailVerificationSentAt.HasValue)
        {
            var cooldownEnd = user.PreferredEmailVerificationSentAt.Value.Plus(Duration.FromMinutes(VerificationCooldownMinutes));
            if (now < cooldownEnd)
            {
                canResend = false;
                minutesUntilResend = (int)Math.Ceiling((cooldownEnd - now).TotalMinutes);
            }
        }

        var isPending = !string.IsNullOrEmpty(user.PreferredEmail) && !user.PreferredEmailVerified;

        return new PreferredEmailViewModel
        {
            OAuthEmail = user.Email ?? string.Empty,
            CurrentPreferredEmail = user.PreferredEmail,
            IsVerified = user.PreferredEmailVerified,
            IsPendingVerification = isPending,
            CanResendVerification = canResend,
            MinutesUntilResend = minutesUntilResend
        };
    }
}
