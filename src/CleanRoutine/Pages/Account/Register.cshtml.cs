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
    public string? Error { get; private set; }
    public bool SignUpOpen => accounts.SignUpOpen;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!SignUpOpen) return Page();
        var (user, error) = await accounts.RegisterAsync(Name, Email, Password);
        if (user is null)
        {
            Error = error;
            return Page();
        }

        await HttpContext.SignInAsync(AccountService.ToPrincipal(user), new AuthenticationProperties { IsPersistent = true });
        return Redirect("/");
    }
}
