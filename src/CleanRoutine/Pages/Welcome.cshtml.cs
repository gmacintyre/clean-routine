using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

public class WelcomeModel : PageModel
{
    public bool Deleted { get; private set; }

    public IActionResult OnGet(bool deleted = false)
    {
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");
        Deleted = deleted;
        return Page();
    }
}
