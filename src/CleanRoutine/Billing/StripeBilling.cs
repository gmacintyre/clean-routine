using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanRoutine.Domain;
using CleanRoutine.Services;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Billing;

/// <summary>
/// The ad-free upgrade, through Stripe Checkout (Stripe hosts the payment page, so card details never touch
/// this app) and the Stripe customer portal (where people cancel or change cards). Talks to Stripe's REST API
/// directly, so there's no package to install. A household turns ad-free when Stripe's webhook says the
/// checkout finished, and goes back to free when the subscription ends.
/// </summary>
public class StripeBilling(IHttpClientFactory http, IOptions<BillingOptions> options, AccountStore accounts,
    TimeProvider time, ILogger<StripeBilling> log)
{
    public const string HttpClientName = "stripe";
    private static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);

    public bool IsConfigured => options.Value.IsConfigured;

    /// <summary>Start a Stripe Checkout page for the household. Returns the URL to send the person to.</summary>
    public async Task<string> StartCheckoutAsync(HouseholdInfo household, string email, string baseUrl)
    {
        var o = options.Value;
        var form = new List<KeyValuePair<string, string>>
        {
            new("mode", o.IsSubscription ? "subscription" : "payment"),
            new("line_items[0][price]", o.StripePriceId!),
            new("line_items[0][quantity]", "1"),
            new("success_url", baseUrl + "/Premium?session_id={CHECKOUT_SESSION_ID}"),
            new("cancel_url", baseUrl + "/Premium"),
            new("client_reference_id", household.Id),
            new("metadata[household_id]", household.Id),
            new("allow_promotion_codes", "true"),
        };
        if (household.StripeCustomerId is { } customer) form.Add(new("customer", customer));
        else form.Add(new("customer_email", email));
        if (o.IsSubscription) form.Add(new("subscription_data[metadata][household_id]", household.Id));
        else if (household.StripeCustomerId is null) form.Add(new("customer_creation", "always"));

        using var doc = await SendAsync(HttpMethod.Post, "v1/checkout/sessions", form);
        return doc.RootElement.GetProperty("url").GetString()!;
    }

    /// <summary>When Stripe sends the person back after paying, check the session straight away so the
    /// ads go without waiting for the webhook. Returns true when the household is now ad-free.</summary>
    public async Task<bool> ConfirmCheckoutAsync(string sessionId, string householdId)
    {
        if (!sessionId.StartsWith("cs_", StringComparison.Ordinal) || sessionId.Length > 255) return false;
        using var doc = await SendAsync(HttpMethod.Get, "v1/checkout/sessions/" + Uri.EscapeDataString(sessionId), null);
        var session = doc.RootElement;
        if (Str(session, "client_reference_id") != householdId) return false;
        return await ApplyCheckoutAsync(session);
    }

    /// <summary>Open Stripe's customer portal, where people cancel, change card or see receipts.</summary>
    public async Task<string> StartPortalAsync(string customerId, string returnUrl)
    {
        using var doc = await SendAsync(HttpMethod.Post, "v1/billing_portal/sessions",
            [new("customer", customerId), new("return_url", returnUrl)]);
        return doc.RootElement.GetProperty("url").GetString()!;
    }

    /// <summary>Handle a webhook call from Stripe. Returns false if the signature doesn't check out.</summary>
    public async Task<bool> HandleWebhookAsync(string payload, string? signatureHeader)
    {
        if (!VerifySignature(payload, signatureHeader, options.Value.StripeWebhookSecret, time.GetUtcNow()))
            return false;

        using var doc = JsonDocument.Parse(payload);
        var evt = doc.RootElement;
        var id = Str(evt, "id") ?? "";
        var type = Str(evt, "type");
        var obj = evt.GetProperty("data").GetProperty("object");

        // Stripe can deliver the same event more than once.
        var seen = await accounts.WriteAsync(a =>
        {
            if (a.RecentStripeEvents.Contains(id)) return true;
            a.RecentStripeEvents.Add(id);
            if (a.RecentStripeEvents.Count > 500) a.RecentStripeEvents.RemoveRange(0, a.RecentStripeEvents.Count - 500);
            return false;
        });
        if (seen) return true;

        switch (type)
        {
            case "checkout.session.completed":
            case "checkout.session.async_payment_succeeded":
                await ApplyCheckoutAsync(obj);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                await ApplySubscriptionAsync(obj);
                break;
        }
        return true;
    }

    private async Task<bool> ApplyCheckoutAsync(JsonElement session)
    {
        var householdId = Str(session, "client_reference_id");
        var complete = Str(session, "status") == "complete"
                       && Str(session, "payment_status") is "paid" or "no_payment_required";
        if (householdId is null || !complete) return false;

        var subscriptionId = Str(session, "subscription");
        var customerId = Str(session, "customer");
        var applied = await accounts.WriteAsync(a =>
        {
            var h = a.Households.FirstOrDefault(x => x.Id == householdId);
            if (h is null) return false;
            h.Plan = Plan.Premium;
            h.StripeCustomerId = customerId ?? h.StripeCustomerId;
            if (subscriptionId is not null)
            {
                if (h.StripeSubscriptionId != subscriptionId) h.SubscriptionStatus = "active";
                h.StripeSubscriptionId = subscriptionId;
            }
            else
            {
                h.SubscriptionStatus = "paid";
            }
            return true;
        });
        if (applied) log.LogInformation("Household {Id} is now ad-free.", householdId);
        return applied;
    }

    private async Task ApplySubscriptionAsync(JsonElement sub)
    {
        var subscriptionId = Str(sub, "id");
        var status = Str(sub, "status");
        var customerId = Str(sub, "customer");
        var householdId = sub.TryGetProperty("metadata", out var meta) ? Str(meta, "household_id") : null;

        await accounts.WriteAsync(a =>
        {
            var h = a.Households.FirstOrDefault(x => x.StripeSubscriptionId == subscriptionId)
                    ?? a.Households.FirstOrDefault(x => x.Id == householdId);
            if (h is null) return;
            // An event about an older subscription the household has since replaced: leave the newer one alone.
            if (h.StripeSubscriptionId is not null && h.StripeSubscriptionId != subscriptionId) return;
            h.StripeSubscriptionId = subscriptionId;
            h.StripeCustomerId = customerId ?? h.StripeCustomerId;
            h.SubscriptionStatus = status;
            h.SubscriptionEnding = sub.TryGetProperty("cancel_at_period_end", out var ending) && ending.ValueKind == JsonValueKind.True
                                   || sub.TryGetProperty("cancel_at", out var cancelAt) && cancelAt.ValueKind == JsonValueKind.Number;
            // past_due: Stripe is retrying the card, so keep the household ad-free meanwhile.
            h.Plan = status is "active" or "trialing" or "past_due" ? Plan.Premium : Plan.Free;
            log.LogInformation("Household {Id} subscription is {Status}.", h.Id, status);
        });
    }

    /// <summary>Checks the Stripe-Signature header: HMAC-SHA256 of "{timestamp}.{payload}" with the
    /// webhook's signing secret, and a timestamp within five minutes.</summary>
    public static bool VerifySignature(string payload, string? header, string? secret, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(secret)) return false;
        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t") timestamp = kv[1];
            else if (kv[0] == "v1") signatures.Add(kv[1]);
        }
        if (!long.TryParse(timestamp, out var seconds)) return false;
        if ((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > SignatureTolerance) return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        foreach (var sig in signatures)
        {
            byte[] given;
            try { given = Convert.FromHexString(sig); }
            catch (FormatException) { continue; }
            if (CryptographicOperations.FixedTimeEquals(given, expected)) return true;
        }
        return false;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, List<KeyValuePair<string, string>>? form)
    {
        var client = http.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.StripeSecretKey);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            log.LogError("Stripe {Path} failed: {Status} {Body}", path, (int)response.StatusCode, body);
            throw new HttpRequestException($"Stripe returned {(int)response.StatusCode}.");
        }
        return JsonDocument.Parse(body);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
