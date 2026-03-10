# Facilitated Messaging Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow any authenticated volunteer to send a one-off plain-text message to another volunteer via email, facilitated by Humans.

**Architecture:** New GET/POST actions on HumanController with a simple form page. Email sent via existing IEmailService/IEmailRenderer pipeline with a new reply-to parameter. Audit logged. "Send a message" button on profile card when viewer can't see any email.

**Tech Stack:** ASP.NET Core MVC, MailKit (existing), IStringLocalizer (existing), IAuditLogService (existing)

**Spec:** `docs/superpowers/specs/2026-03-10-facilitated-messaging-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Humans.Domain/Enums/AuditAction.cs` | Modify | Add `FacilitatedMessageSent` enum value |
| `src/Humans.Application/Interfaces/IEmailRenderer.cs` | Modify | Add `RenderFacilitatedMessage` method |
| `src/Humans.Application/Interfaces/IEmailService.cs` | Modify | Add `SendFacilitatedMessageAsync` method |
| `src/Humans.Infrastructure/Services/EmailRenderer.cs` | Modify | Implement `RenderFacilitatedMessage` |
| `src/Humans.Infrastructure/Services/SmtpEmailService.cs` | Modify | Implement `SendFacilitatedMessageAsync` with reply-to support |
| `src/Humans.Infrastructure/Services/StubEmailService.cs` | Modify | Stub implementation |
| `src/Humans.Web/Models/SendMessageViewModel.cs` | Create | Form view model |
| `src/Humans.Web/Controllers/HumanController.cs` | Modify | Add GET/POST `SendMessage` actions |
| `src/Humans.Web/Views/Human/SendMessage.cshtml` | Create | Form page |
| `src/Humans.Web/Views/Shared/Components/ProfileCard/Default.cshtml` | Modify | Add "Send a message" button |
| `src/Humans.Web/Models/ProfileCardViewModel.cs` | Modify | Add `CanSendMessage` property |
| `src/Humans.Web/Resources/SharedResource.resx` | Modify | Add localization keys |
| `src/Humans.Web/Resources/SharedResource.es.resx` | Modify | Add Spanish translations |

---

## Task 1: Add AuditAction Enum Value

**Files:**
- Modify: `src/Humans.Domain/Enums/AuditAction.cs:34`

- [ ] **Step 1: Add enum value**

Add `FacilitatedMessageSent` after the last value (`GoogleResourceDeactivated`):

```csharp
    GoogleResourceDeactivated,
    FacilitatedMessageSent,
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Humans.Domain/Humans.Domain.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Humans.Domain/Enums/AuditAction.cs
git commit -m "feat: add FacilitatedMessageSent audit action"
```

---

## Task 2: Add Email Renderer Method

**Files:**
- Modify: `src/Humans.Application/Interfaces/IEmailRenderer.cs`
- Modify: `src/Humans.Infrastructure/Services/EmailRenderer.cs`

- [ ] **Step 1: Add interface method**

Add to `IEmailRenderer` after the last method:

```csharp
/// <summary>
/// Facilitated message between volunteers.
/// </summary>
EmailContent RenderFacilitatedMessage(
    string recipientName,
    string senderName,
    string messageText,
    bool includeContactInfo,
    string? senderEmail,
    string? culture = null);
```

- [ ] **Step 2: Implement in EmailRenderer**

Add to `EmailRenderer.cs` after the last render method. The message text must be HTML-encoded and have newlines converted to `<br>`. No HTML/markdown from the user is rendered.

```csharp
public EmailContent RenderFacilitatedMessage(
    string recipientName,
    string senderName,
    string messageText,
    bool includeContactInfo,
    string? senderEmail,
    string? culture = null)
{
    using (WithCulture(culture))
    {
        var subject = string.Format(
            CultureInfo.CurrentCulture,
            _localizer["Email_FacilitatedMessage_Subject"].Value,
            senderName);

        var sanitizedMessage = HtmlEncode(messageText).Replace("\n", "<br />");

        var contactInfoHtml = includeContactInfo && !string.IsNullOrEmpty(senderEmail)
            ? $"<p><strong>{HtmlEncode(senderName)}</strong> &mdash; <a href=\"mailto:{HtmlEncode(senderEmail)}\">{HtmlEncode(senderEmail)}</a></p>"
            : $"<p><em>{HtmlEncode(_localizer["Email_FacilitatedMessage_NoContactInfo"].Value)}</em></p>";

        var body = string.Format(
            CultureInfo.CurrentCulture,
            _localizer["Email_FacilitatedMessage_Body"].Value,
            HtmlEncode(recipientName),
            HtmlEncode(senderName),
            sanitizedMessage,
            contactInfoHtml);

        return new EmailContent(subject, body);
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded (will fail until SmtpEmailService and StubEmailService implement the new interface method — that's Task 3)

---

## Task 3: Add Email Service Methods with Reply-To Support

**Files:**
- Modify: `src/Humans.Application/Interfaces/IEmailService.cs`
- Modify: `src/Humans.Infrastructure/Services/SmtpEmailService.cs`
- Modify: `src/Humans.Infrastructure/Services/StubEmailService.cs`

- [ ] **Step 1: Add interface method**

Add to `IEmailService` after the last method:

```csharp
/// <summary>
/// Sends a facilitated message from one volunteer to another.
/// </summary>
/// <param name="recipientEmail">The recipient's notification target email.</param>
/// <param name="recipientName">The recipient's display name.</param>
/// <param name="senderName">The sender's display name.</param>
/// <param name="messageText">Plain text message body.</param>
/// <param name="includeContactInfo">Whether to include sender's contact info.</param>
/// <param name="senderEmail">The sender's email (used for reply-to and contact info).</param>
/// <param name="culture">The recipient's preferred culture (ISO code).</param>
/// <param name="cancellationToken">Cancellation token.</param>
Task SendFacilitatedMessageAsync(
    string recipientEmail,
    string recipientName,
    string senderName,
    string messageText,
    bool includeContactInfo,
    string? senderEmail,
    string? culture = null,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Add private SendEmailAsync overload with reply-to in SmtpEmailService**

Add a new overload of the private `SendEmailAsync` method that accepts an optional `replyTo` parameter. Place it right after the existing `SendEmailAsync` method (after line 273):

```csharp
private async Task SendEmailAsync(
    string toAddress,
    string subject,
    string htmlBody,
    string? replyTo,
    CancellationToken cancellationToken)
{
    try
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(toAddress));
        message.Subject = subject;

        if (!string.IsNullOrEmpty(replyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = WrapInTemplate(htmlBody),
            TextBody = HtmlToPlainText(htmlBody)
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(
            _settings.SmtpHost,
            _settings.SmtpPort,
            _settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        if (!string.IsNullOrEmpty(_settings.Username))
        {
            await client.AuthenticateAsync(_settings.Username, _settings.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Email sent to {To}: {Subject}", toAddress, subject);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to send email to {To}: {Subject}", toAddress, subject);
        throw;
    }
}
```

- [ ] **Step 3: Implement SendFacilitatedMessageAsync in SmtpEmailService**

```csharp
public async Task SendFacilitatedMessageAsync(
    string recipientEmail,
    string recipientName,
    string senderName,
    string messageText,
    bool includeContactInfo,
    string? senderEmail,
    string? culture = null,
    CancellationToken cancellationToken = default)
{
    var content = _renderer.RenderFacilitatedMessage(
        recipientName, senderName, messageText, includeContactInfo, senderEmail, culture);
    var replyTo = includeContactInfo ? senderEmail : null;
    await SendEmailAsync(recipientEmail, content.Subject, content.HtmlBody, replyTo, cancellationToken);
    _metrics.RecordEmailSent("facilitated_message");
}
```

- [ ] **Step 4: Add stub implementation in StubEmailService**

```csharp
public Task SendFacilitatedMessageAsync(
    string recipientEmail,
    string recipientName,
    string senderName,
    string messageText,
    bool includeContactInfo,
    string? senderEmail,
    string? culture = null,
    CancellationToken cancellationToken = default)
{
    _logger.LogInformation(
        "[STUB] Would send facilitated message to {Email} ({RecipientName}) from {SenderName} [Culture: {Culture}] [IncludeContactInfo: {IncludeContact}]",
        recipientEmail, recipientName, senderName, culture, includeContactInfo);
    return Task.CompletedTask;
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/Humans.Application/Interfaces/IEmailRenderer.cs src/Humans.Application/Interfaces/IEmailService.cs src/Humans.Infrastructure/Services/EmailRenderer.cs src/Humans.Infrastructure/Services/SmtpEmailService.cs src/Humans.Infrastructure/Services/StubEmailService.cs
git commit -m "feat: add facilitated message email with reply-to support"
```

---

## Task 4: Add View Model and Controller Actions

**Files:**
- Create: `src/Humans.Web/Models/SendMessageViewModel.cs`
- Modify: `src/Humans.Web/Controllers/HumanController.cs`

- [ ] **Step 1: Create the view model**

Create `src/Humans.Web/Models/SendMessageViewModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Humans.Web.Models;

public class SendMessageViewModel
{
    public Guid RecipientId { get; set; }
    public string RecipientDisplayName { get; set; } = string.Empty;

    [Required]
    [StringLength(2000, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    public bool IncludeContactInfo { get; set; } = true;
}
```

- [ ] **Step 2: Add IEmailService and IUserEmailService to HumanController**

The controller already has `IProfileService`, `UserManager<User>`, `IAuditLogService`, `IClock`, and `IStringLocalizer<SharedResource>`. Add `IEmailService` and `IUserEmailService` to the constructor injection:

```csharp
private readonly IEmailService _emailService;
private readonly IUserEmailService _userEmailService;
```

Add to constructor parameters and assign in body. Also add the required `using Humans.Application.Interfaces;` if not already present.

- [ ] **Step 3: Add GET action**

Add after the existing `View` action:

```csharp
[HttpGet("{id:guid}/SendMessage")]
public async Task<IActionResult> SendMessage(Guid id)
{
    var currentUser = await _userManager.GetUserAsync(User);
    if (currentUser == null)
        return NotFound();

    if (currentUser.Id == id)
        return RedirectToAction(nameof(View), new { id });

    var targetUser = await _userManager.FindByIdAsync(id.ToString());
    if (targetUser == null)
        return NotFound();

    var viewModel = new SendMessageViewModel
    {
        RecipientId = id,
        RecipientDisplayName = targetUser.DisplayName
    };

    return View(viewModel);
}
```

- [ ] **Step 4: Add POST action**

```csharp
[HttpPost("{id:guid}/SendMessage")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SendMessage(Guid id, SendMessageViewModel model)
{
    var currentUser = await _userManager.GetUserAsync(User);
    if (currentUser == null)
        return NotFound();

    if (currentUser.Id == id)
        return RedirectToAction(nameof(View), new { id });

    var targetUser = await _userManager.Users
        .Include(u => u.UserEmails)
        .FirstOrDefaultAsync(u => u.Id == id);
    if (targetUser == null)
        return NotFound();

    model.RecipientId = id;
    model.RecipientDisplayName = targetUser.DisplayName;

    if (!ModelState.IsValid)
        return View(model);

    // Strip any HTML tags from the message for safety
    var cleanMessage = System.Text.RegularExpressions.Regex.Replace(
        model.Message, "<[^>]+>", "", System.Text.RegularExpressions.RegexOptions.None,
        TimeSpan.FromSeconds(1));

    var sender = await _userManager.Users
        .Include(u => u.UserEmails)
        .FirstAsync(u => u.Id == currentUser.Id);

    var recipientEmail = targetUser.GetEffectiveEmail() ?? targetUser.Email!;
    var senderEmail = sender.GetEffectiveEmail() ?? sender.Email!;

    await _emailService.SendFacilitatedMessageAsync(
        recipientEmail,
        targetUser.DisplayName,
        sender.DisplayName,
        cleanMessage,
        model.IncludeContactInfo,
        senderEmail,
        targetUser.PreferredLanguage);

    await _auditLogService.LogAsync(
        AuditAction.FacilitatedMessageSent,
        "User", targetUser.Id,
        $"Message sent to {targetUser.DisplayName} (contact info shared: {(model.IncludeContactInfo ? "yes" : "no")})",
        currentUser.Id, currentUser.DisplayName);

    TempData["SuccessMessage"] = string.Format(
        _localizer["SendMessage_Success"].Value,
        targetUser.DisplayName);

    return RedirectToAction(nameof(View), new { id });
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/Humans.Web/Models/SendMessageViewModel.cs src/Humans.Web/Controllers/HumanController.cs
git commit -m "feat: add SendMessage controller actions and view model"
```

---

## Task 5: Create the Form View

**Files:**
- Create: `src/Humans.Web/Views/Human/SendMessage.cshtml`

- [ ] **Step 1: Create the form view**

Create `src/Humans.Web/Views/Human/SendMessage.cshtml` following the EditTeam pattern:

```html
@model Humans.Web.Models.SendMessageViewModel
@{
    ViewData["Title"] = string.Format(Localizer["SendMessage_Title"].Value, Model.RecipientDisplayName);
}

<nav aria-label="breadcrumb">
    <ol class="breadcrumb">
        <li class="breadcrumb-item"><a asp-controller="Team" asp-action="Index">@Localizer["Teams_Title"]</a></li>
        <li class="breadcrumb-item"><a asp-action="View" asp-route-id="@Model.RecipientId">@Model.RecipientDisplayName</a></li>
        <li class="breadcrumb-item active" aria-current="page">@Localizer["SendMessage_Breadcrumb"]</li>
    </ol>
</nav>

<h1>@string.Format(Localizer["SendMessage_Heading"].Value, Model.RecipientDisplayName)</h1>

<vc:temp-data-alerts />

<div class="card">
    <div class="card-body">
        <form asp-action="SendMessage" asp-route-id="@Model.RecipientId" method="post">
            @Html.AntiForgeryToken()
            <input type="hidden" asp-for="RecipientId" />
            <input type="hidden" asp-for="RecipientDisplayName" />

            <div asp-validation-summary="ModelOnly" class="alert alert-danger"></div>

            <div class="mb-3">
                <label asp-for="Message" class="form-label">@Localizer["SendMessage_MessageLabel"]</label>
                <textarea asp-for="Message" class="form-control" rows="8" maxlength="2000"></textarea>
                <span asp-validation-for="Message" class="text-danger"></span>
                <div class="form-text">@Localizer["SendMessage_PlainTextOnly"]</div>
            </div>

            <div class="mb-3 form-check">
                <input asp-for="IncludeContactInfo" class="form-check-input" type="checkbox" />
                <label asp-for="IncludeContactInfo" class="form-check-label">@Localizer["SendMessage_IncludeContactInfo"]</label>
                <div class="form-text">@Localizer["SendMessage_IncludeContactInfoHelp"]</div>
            </div>

            <div class="d-flex gap-2">
                <button type="submit" class="btn btn-primary">@Localizer["SendMessage_Send"]</button>
                <a asp-action="View" asp-route-id="@Model.RecipientId" class="btn btn-outline-secondary">@Localizer["Common_Cancel"]</a>
            </div>
        </form>
    </div>
</div>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Humans.Web/Views/Human/SendMessage.cshtml
git commit -m "feat: add SendMessage form view"
```

---

## Task 6: Add Profile Card Button

**Files:**
- Modify: `src/Humans.Web/Models/ProfileCardViewModel.cs`
- Modify: `src/Humans.Web/ViewComponents/ProfileCardViewComponent.cs`
- Modify: `src/Humans.Web/Views/Shared/Components/ProfileCard/Default.cshtml`

- [ ] **Step 1: Add CanSendMessage property to ProfileCardViewModel**

Add after the `PublicUserEmails` property (around line 79):

```csharp
/// <summary>
/// Whether the viewer should see the "Send a message" button.
/// True when viewing another user's profile and no email is visible.
/// </summary>
public bool CanSendMessage { get; set; }
```

- [ ] **Step 2: Set CanSendMessage in ProfileCardViewComponent**

In `ProfileCardViewComponent.cs`, after the `visibleEmails` variable is populated and before the model is returned, compute and set:

```csharp
var hasVisibleEmail = visibleEmails.Any()
    || visibleContactFields.Any(f => f.FieldType == ContactFieldType.Email);
```

Then in the model initialization, set:

```csharp
CanSendMessage = viewMode == ProfileCardViewMode.Public && !hasVisibleEmail,
```

Note: Check how `visibleContactFields` is named in the existing code — it may be `contactFields` or similar. Match the existing variable name.

- [ ] **Step 3: Add button to profile card view**

In `Default.cshtml`, after the contact info section (after line 124, after the `else if (Model.IsOwnProfile)` block), add:

```html
@if (Model.CanSendMessage)
{
    <hr />
    <a asp-controller="Human" asp-action="SendMessage" asp-route-id="@Model.UserId"
       class="btn btn-outline-primary w-100">
        <i class="fa-solid fa-envelope me-1"></i>
        @string.Format(Localizer["SendMessage_Button"].Value, Model.DisplayName)
    </a>
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Humans.Web/Models/ProfileCardViewModel.cs src/Humans.Web/ViewComponents/ProfileCardViewComponent.cs src/Humans.Web/Views/Shared/Components/ProfileCard/Default.cshtml
git commit -m "feat: add Send Message button to profile card"
```

---

## Task 7: Add Localization Keys

**Files:**
- Modify: `src/Humans.Web/Resources/SharedResource.resx`
- Modify: `src/Humans.Web/Resources/SharedResource.es.resx`

- [ ] **Step 1: Add English localization keys to SharedResource.resx**

Add these entries:

| Key | Value | Comment |
|-----|-------|---------|
| `Email_FacilitatedMessage_Subject` | `Humans Message from: {0}` | {0} = sender name |
| `Email_FacilitatedMessage_Body` | `<h2>Message from {1}</h2><p>Hi {0},</p><p>{1} has sent you a message through Humans:</p><hr /><p>{2}</p><hr />{3}` | {0}=recipient, {1}=sender, {2}=message, {3}=contact info html |
| `Email_FacilitatedMessage_NoContactInfo` | `This human chose not to share their contact information.` | |
| `SendMessage_Title` | `Send {0} a message` | {0} = recipient name |
| `SendMessage_Breadcrumb` | `Send Message` | |
| `SendMessage_Heading` | `Send {0} a message` | {0} = recipient name |
| `SendMessage_MessageLabel` | `Message` | |
| `SendMessage_PlainTextOnly` | `Plain text only. Maximum 2000 characters.` | |
| `SendMessage_IncludeContactInfo` | `Include my contact info` | |
| `SendMessage_IncludeContactInfoHelp` | `Your name and email will be included so they can reply to you directly.` | |
| `SendMessage_Send` | `Send Message` | |
| `SendMessage_Success` | `Your message has been sent to {0}.` | {0} = recipient name |
| `SendMessage_Button` | `Send {0} a message` | {0} = display name |

- [ ] **Step 2: Add Spanish translations to SharedResource.es.resx**

| Key | Value |
|-----|-------|
| `Email_FacilitatedMessage_Subject` | `Mensaje de Humans de: {0}` |
| `Email_FacilitatedMessage_Body` | `<h2>Mensaje de {1}</h2><p>Hola {0},</p><p>{1} te ha enviado un mensaje a través de Humans:</p><hr /><p>{2}</p><hr />{3}` |
| `Email_FacilitatedMessage_NoContactInfo` | `Este human ha decidido no compartir su información de contacto.` |
| `SendMessage_Title` | `Enviar un mensaje a {0}` |
| `SendMessage_Breadcrumb` | `Enviar mensaje` |
| `SendMessage_Heading` | `Enviar un mensaje a {0}` |
| `SendMessage_MessageLabel` | `Mensaje` |
| `SendMessage_PlainTextOnly` | `Solo texto. Máximo 2000 caracteres.` |
| `SendMessage_IncludeContactInfo` | `Incluir mi información de contacto` |
| `SendMessage_IncludeContactInfoHelp` | `Tu nombre y correo electrónico se incluirán para que puedan responderte directamente.` |
| `SendMessage_Send` | `Enviar mensaje` |
| `SendMessage_Success` | `Tu mensaje ha sido enviado a {0}.` |
| `SendMessage_Button` | `Enviar un mensaje a {0}` |

- [ ] **Step 3: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Humans.Web/Resources/SharedResource.resx src/Humans.Web/Resources/SharedResource.es.resx
git commit -m "feat: add localization keys for facilitated messaging"
```

---

## Task 8: Add to Email Preview

**Files:**
- Modify: `src/Humans.Web/Controllers/AdminController.cs` (EmailPreview action)

- [ ] **Step 1: Add facilitated message to email preview**

In the `EmailPreview` action, after the last `items.Add(...)` call, add:

```csharp
var c_msg = renderer.RenderFacilitatedMessage(displayName, "Alex Firestone", "Hi! I'm organizing the next community event and would love your help. Let me know if you're interested!", true, "alex@example.com", culture);
items.Add(new EmailPreviewItem { Id = "facilitated-message", Name = "Facilitated Message (with contact info)", Recipient = email, Subject = c_msg.Subject, Body = c_msg.HtmlBody });

var c_msg2 = renderer.RenderFacilitatedMessage(displayName, "Alex Firestone", "Hi! I'm organizing the next community event and would love your help. Let me know if you're interested!", false, null, culture);
items.Add(new EmailPreviewItem { Id = "facilitated-message-anon", Name = "Facilitated Message (without contact info)", Recipient = email, Subject = c_msg2.Subject, Body = c_msg2.HtmlBody });
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Humans.Web/Controllers/AdminController.cs
git commit -m "feat: add facilitated message to email preview"
```

---

## Task 9: Final Verification

- [ ] **Step 1: Full build**

Run: `dotnet build Humans.slnx`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Run tests**

Run: `dotnet test Humans.slnx`
Expected: All tests pass

- [ ] **Step 3: Manual smoke test checklist**

After deploying to QA:
1. Visit a profile where you can't see any email → "Send a message" button visible
2. Visit a profile where you CAN see an email → no button
3. Visit your own profile → no button
4. Click "Send a message" → form loads with correct name
5. Submit with empty message → validation error
6. Submit with text + contact info checked → success, redirect to profile
7. Submit with text + contact info unchecked → success
8. Check email preview at `/Admin/EmailPreview` → both variants render correctly
9. Check audit log → FacilitatedMessageSent entries visible
