using System.Net;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Pages.Account;

/// <summary>Ask for a password-reset link. The answer is the same whether or not the email has an account,
/// so the form can't be used to find out who has signed up.</summary>
[EnableRateLimiting("signin")]
public class ForgotModel(AccountService accounts, EmailSender email, IOptions<SiteOptions> site,
    TimeProvider time, IHostEnvironment env, ILogger<ForgotModel> log) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    public bool Sent { get; private set; }
    public bool CanEmail => email.IsConfigured || env.IsDevelopment();
    public string ContactEmail => site.Value.ContactEmail;

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!CanEmail || string.IsNullOrWhiteSpace(Email)) return Page();
        Sent = true;

        var reset = await accounts.StartResetAsync(Email, time.GetUtcNow());
        if (reset is not { } r) return Page();

        var baseUrl = string.IsNullOrWhiteSpace(site.Value.BaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : site.Value.BaseUrl.TrimEnd('/');
        var link = $"{baseUrl}/Account/Reset?u={r.UserId}&t={r.Token}";

        if (!email.IsConfigured)
        {
            log.LogWarning("Email isn't set up; development reset link for {Email}: {Link}", r.Email, link);
            return Page();
        }

        var name = WebUtility.HtmlEncode(r.Name);
        var text = $"""
            Hi {r.Name},

            Someone (hopefully you) asked to reset your Apron Strings password. Open this link within an hour to choose a new one:

            {link}

            If you didn't ask, ignore this email. Your password stays the same.
            """;
        var html = $"""
            <p>Hi {name},</p>
            <p>Someone (hopefully you) asked to reset your Apron Strings password. Open this link within an hour to choose a new one:</p>
            <p><a href="{link}" style="display:inline-block;background:#2F5DA8;color:#fff;padding:10px 16px;border-radius:10px;text-decoration:none">Choose a new password</a></p>
            <p style="color:#5D6A63;font-size:13px">If you didn't ask, ignore this email. Your password stays the same.</p>
            """;
        try
        {
            await email.SendAsync(r.Email, "Reset your Apron Strings password", text, html);
        }
        catch (Exception ex)
        {
            // Same answer either way; the log says what went wrong.
            log.LogError(ex, "Couldn't send the password reset email.");
        }
        return Page();
    }
}
