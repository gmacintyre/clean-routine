using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

/// <summary>Where invite links land. New people go to sign-up; someone already signed in confirms moving
/// into the household.</summary>
public class JoinModel(AccountService accounts, CurrentHousehold current) : PageModel
{
    [FromRoute] public string Code { get; set; } = "";
    public string? HouseholdName { get; private set; }
    public string? CurrentName { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Account/Register", new { invite = Code });

        var invite = await accounts.FindInviteAsync(Code);
        if (invite is null)
        {
            Error = "That invite link has expired. Ask for a new one.";
            return Page();
        }
        if (invite.Value.Id == User.HouseholdId()) return Redirect("/");
        HouseholdName = invite.Value.Name;
        CurrentName = (await current.GetAsync()).Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return RedirectToPage("/Account/Register", new { invite = Code });
        var error = await accounts.JoinAsync(User.UserId(), Code);
        if (error is not null)
        {
            Error = error;
            return Page();
        }
        // The sign-in cookie still names the old household; it's refreshed on the next request.
        TempData["Toast"] = "You've joined the household";
        return Redirect("/");
    }
}
