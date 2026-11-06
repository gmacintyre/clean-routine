using CleanRoutine.Billing;
using CleanRoutine.Domain;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Pages;

/// <summary>The ad-free upgrade: what it is, the button that goes to Stripe Checkout, and billing management.</summary>
public class PremiumModel(StripeBilling billing, CurrentHousehold household, IOptions<BillingOptions> options,
    ILogger<PremiumModel> log) : PageModel
{
    public HouseholdInfo Household { get; private set; } = new();
    public bool CanBuy => billing.IsConfigured;
    public string PriceLabel => options.Value.PriceLabel;
    public bool IsSubscription => options.Value.IsSubscription;
    public string? Error { get; private set; }

    public async Task OnGetAsync(string? session_id)
    {
        Household = await household.GetAsync();
        if (session_id is not null && !Household.IsAdFree && CanBuy)
        {
            try
            {
                if (await billing.ConfirmCheckoutAsync(session_id, Household.Id))
                    TempData["Toast"] = "You're ad-free. Thank you!";
            }
            catch (Exception ex)
            {
                // The webhook will catch up; nothing for the person to do.
                log.LogWarning(ex, "Couldn't confirm checkout {Session}.", session_id);
            }
            Household = await ReloadAsync() ?? Household;
        }
    }

    public async Task<IActionResult> OnPostBuyAsync()
    {
        Household = await household.GetAsync();
        if (Household.IsAdFree) return RedirectToPage();
        if (!CanBuy) return RedirectToPage();
        try
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            return Redirect(await billing.StartCheckoutAsync(Household, email, BaseUrl));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Couldn't start checkout.");
            Error = "Payments aren't working right now. Please try again in a few minutes.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostManageAsync()
    {
        Household = await household.GetAsync();
        if (Household.StripeCustomerId is null || !CanBuy) return RedirectToPage();
        try
        {
            return Redirect(await billing.StartPortalAsync(Household.StripeCustomerId, BaseUrl + "/Premium"));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Couldn't open the billing portal.");
            Error = "Billing isn't available right now. Please try again in a few minutes.";
            return Page();
        }
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    // CurrentHousehold caches per request; read again after a change.
    private Task<HouseholdInfo?> ReloadAsync() =>
        HttpContext.RequestServices.GetRequiredService<AccountStore>().GetHouseholdAsync(User.HouseholdId());
}
