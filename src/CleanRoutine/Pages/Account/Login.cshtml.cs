using CleanRoutine.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace CleanRoutine.Pages.Account;

[EnableRateLimiting("signin")]
public class LoginModel(AccountService accounts) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    public string? Error { get; private set; }
    public bool SignUpOpen => accounts.SignUpOpen;

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? Redirect("/") : Page();

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        var user = await accounts.ValidateAsync(Email, Password);
        if (user is null)
        {
            Error = "That email and password don't match. Check them and try again.";
            return Page();
        }

        await HttpContext.SignInAsync(AccountService.ToPrincipal(user), new AuthenticationProperties { IsPersistent = true });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
