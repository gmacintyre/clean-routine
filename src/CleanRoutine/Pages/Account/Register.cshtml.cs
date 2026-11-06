using CleanRoutine.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanRoutine.Pages.Account;

[EnableRateLimiting("signin")]
public class RegisterModel(AccountService accounts) : PageModel
{
    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    /// <summary>Filled in by the browser so a new household starts in the right time zone.</summary>
    [BindProperty] public string? TimeZone { get; set; }
    [BindProperty] public bool AcceptTerms { get; set; }
    [BindProperty(SupportsGet = true)] public string? Invite { get; set; }

    public string? Error { get; private set; }
    /// <summary>Name of the household the invite link belongs to, when there is one.</summary>
    public string? JoiningName { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
            return Invite is null ? Redirect("/") : Redirect("/Join/" + Uri.EscapeDataString(Invite));
        await LoadInviteAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadInviteAsync();
        if (!AcceptTerms)
        {
            Error = "Please agree to the terms and privacy policy.";
            return Page();
        }
        var (user, error) = await accounts.RegisterAsync(Name, Email, Password, Invite, TimeZone);
        if (user is null)
        {
            Error = error;
            return Page();
        }

        await HttpContext.SignInAsync(AccountService.ToPrincipal(user), new AuthenticationProperties { IsPersistent = true });
        TempData["Toast"] = Invite is null ? "Welcome! We've set you up with a starter routine." : $"Welcome to {JoiningName}!";
        return Redirect("/");
    }

    private async Task LoadInviteAsync()
    {
        if (string.IsNullOrWhiteSpace(Invite)) { Invite = null; return; }
        if (await accounts.FindInviteAsync(Invite) is { } h) JoiningName = h.Name;
        else
        {
            Error = "That invite link has expired. Ask for a new one, or sign up below to start your own household.";
            Invite = null;
        }
    }
}
