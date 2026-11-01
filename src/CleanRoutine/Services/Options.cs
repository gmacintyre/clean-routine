namespace CleanRoutine.Services;

public class HouseholdOptions
{
    /// <summary>Time zone for a new household when the browser doesn't report one, and for the original
    /// household when it's moved to the multi-household layout.</summary>
    public string TimeZone { get; set; } = "America/New_York";
}

public class StorageOptions
{
    /// <summary>Folder for data files. Leave empty to use %HOME%/data/clean-routine on Azure
    /// App Service (persistent storage) or ./App_Data when running locally.</summary>
    public string? DataDirectory { get; set; }
}

public class PushOptions
{
    /// <summary>VAPID public key (base64url). Generate with: dotnet run -- generate-vapid-keys</summary>
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    /// <summary>Contact for push services, e.g. "mailto:you@example.com".</summary>
    public string Subject { get; set; } = "mailto:admin@example.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

public class ReminderOptions
{
    /// <summary>Shared secret the scheduled GitHub job sends in the X-Reminder-Key header to wake the
    /// app and send due reminders. Leave empty to turn the wake-up link off.</summary>
    public string? TriggerKey { get; set; }
}

public class SiteOptions
{
    /// <summary>Who runs the app, shown on the privacy and terms pages.</summary>
    public string OperatorName { get; set; } = "";
    /// <summary>Where people write with questions, privacy requests and account deletion.</summary>
    public string ContactEmail { get; set; } = "";
}

public class AdsOptions
{
    /// <summary>Google AdSense publisher id, like "ca-pub-1234567890123456". Empty: no AdSense ads.</summary>
    public string? AdSenseClientId { get; set; }
    /// <summary>AdSense ad unit ids by placement: "today", "calendar", "routine".</summary>
    public Dictionary<string, string> Slots { get; set; } = [];
    /// <summary>When AdSense isn't set up (or a placement has no unit), show a small "go ad-free" card instead.</summary>
    public bool HouseAds { get; set; } = true;

    public bool AdSenseConfigured => !string.IsNullOrWhiteSpace(AdSenseClientId);
    /// <summary>"pub-…" part of the client id, for ads.txt.</summary>
    public string? PublisherId => AdSenseClientId?.Replace("ca-", "", StringComparison.Ordinal);
}

public class BillingOptions
{
    /// <summary>Stripe secret key (sk_live_… or sk_test_…). Keep it out of the repo.</summary>
    public string? StripeSecretKey { get; set; }
    /// <summary>Signing secret of the Stripe webhook endpoint (whsec_…).</summary>
    public string? StripeWebhookSecret { get; set; }
    /// <summary>Stripe price id (price_…) for the ad-free upgrade.</summary>
    public string? StripePriceId { get; set; }
    /// <summary>"subscription" for a recurring price, or "payment" for a one-time price.</summary>
    public string Mode { get; set; } = "subscription";
    /// <summary>Price as shown in the app, e.g. "$1.99 a month". Must match the Stripe price.</summary>
    public string PriceLabel { get; set; } = "$1.99 a month";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(StripeSecretKey) && !string.IsNullOrWhiteSpace(StripePriceId);
    public bool IsSubscription => !string.Equals(Mode, "payment", StringComparison.OrdinalIgnoreCase);
}
