using CleanRoutine.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanRoutine.Pages.Account;

/// <summary>Where the reset email's link lands: choose a new password, then you're signed in.</summary>
[EnableRateLimiting("signin")]
public class ResetModel(AccountService accounts, TimeProvider time) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "u")] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true, Name = "t")] public string? Token { get; set; }
    [BindProperty] public string Password { get; set; } = "";
    public bool Valid { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        // Keep the token out of the Referer header sent to fonts or anything else the page loads.
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Valid = await accounts.IsResetValidAsync(UserId, Token, time.GetUtcNow());
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var (user, error) = await accounts.ResetPasswordAsync(UserId, Token, Password, time.GetUtcNow());
        if (user is null)
        {
            Error = error;
            Valid = await accounts.IsResetValidAsync(UserId, Token, time.GetUtcNow());
            return Page();
        }

        await HttpContext.SignInAsync(AccountService.ToPrincipal(user), new AuthenticationProperties { IsPersistent = true });
        TempData["Toast"] = "Password changed. You're signed in.";
        return Redirect("/");
    }
}
