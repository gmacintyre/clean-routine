using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Pages;

public class PrivacyModel(IOptions<SiteOptions> site) : PageModel
{
    public SiteOptions Site => site.Value;
    public bool IsDraft => string.IsNullOrWhiteSpace(Site.OperatorName) || string.IsNullOrWhiteSpace(Site.ContactEmail);
}

public class TermsModel(IOptions<SiteOptions> site, IOptions<BillingOptions> billing) : PageModel
{
    public SiteOptions Site => site.Value;
    public BillingOptions Billing => billing.Value;
    public bool IsDraft => string.IsNullOrWhiteSpace(Site.OperatorName) || string.IsNullOrWhiteSpace(Site.ContactEmail);
}
