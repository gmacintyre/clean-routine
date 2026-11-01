using System.Text.Json.Serialization;
using CleanRoutine.Domain;
using CleanRoutine.Push;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Pages;

public class SettingsModel(AccountStore accounts, HouseholdStore store, AccountService accountService, WebPushSender push,
    DayPlanner planner, HouseholdClock clock, CurrentHousehold household, IOptions<ReminderOptions> reminderOptions)
    : PageModel
{
    /// <summary>True when the app relies on the scheduled GitHub job (no Always On), which only runs in the morning.</summary>
    public bool UsesWakeUpJob => !string.IsNullOrWhiteSpace(reminderOptions.Value.TriggerKey);

    public bool PushConfigured => push.IsConfigured;
    public string? PublicKey => push.PublicKey;
    public HouseholdInfo Household { get; private set; } = new();
    public List<(string Name, int Devices, bool Me)> People { get; private set; } = [];
    public string InviteUrl => $"{Request.Scheme}://{Request.Host}/Join/{Household.InviteCode}";
    public string? Error { get; private set; }

    /// <summary>IANA time zone ids for the picker.</summary>
    public static readonly Lazy<string[]> TimeZones = new(() => TimeZoneInfo.GetSystemTimeZones()
        .Select(z => z.HasIanaId ? z.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : null)
        .OfType<string>().Where(id => id.Contains('/')).Distinct().Order().ToArray());

    public async Task OnGetAsync()
    {
        Household = await household.GetAsync();
        var devices = await store.ReadAsync(Household.Id, d =>
            d.PushSubscriptions.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.Count()));
        var me = User.UserId();
        People = await accounts.ReadAsync(a => a.Users.Where(u => u.HouseholdId == Household.Id)
            .Select(u => (u.DisplayName, devices.GetValueOrDefault(u.Id), u.Id == me)).ToList());
    }

    public async Task<IActionResult> OnPostReminderAsync(bool enabled, TimeOnly time)
    {
        var tz = (await household.GetAsync()).TimeZone;
        await UpdateHouseholdAsync(h =>
        {
            h.Reminders.Enabled = enabled;
            if (h.Reminders.Time != time)
            {
                h.Reminders.Time = time;
                // Moving the time later than now lets today's reminder go out at the new time.
                if (TimeOnly.FromDateTime(clock.Now(tz)) < time) h.Reminders.LastSentDate = null;
            }
        });
        TempData["Toast"] = enabled ? $"Reminders set for {time:h:mm tt}" : "Daily reminders turned off";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostHouseholdAsync(string? name, string? timeZone)
    {
        name = (name ?? "").Trim();
        if (name.Length > 40) name = name[..40];
        var validZone = HouseholdClock.IsValidZone(timeZone);
        await UpdateHouseholdAsync(h =>
        {
            if (name.Length > 0) h.Name = name;
            if (validZone && h.TimeZone != timeZone)
            {
                h.TimeZone = timeZone!;
                h.Reminders.LastSentDate = null;
            }
        });
        TempData["Toast"] = validZone || string.IsNullOrWhiteSpace(timeZone) ? "Household saved" : "Saved, but that time zone wasn't recognized";
        return RedirectToPage(null, null, "household");
    }

    public async Task<IActionResult> OnPostNewInviteAsync()
    {
        await UpdateHouseholdAsync(h => h.InviteCode = HouseholdInfo.NewInviteCode());
        TempData["Toast"] = "New invite link made. The old one no longer works.";
        return RedirectToPage(null, null, "household");
    }

    public async Task<IActionResult> OnPostDeleteAccountAsync(string? confirm)
    {
        if (!string.Equals(confirm?.Trim(), "delete", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Toast"] = "Type DELETE to confirm.";
            return RedirectToPage(null, null, "account");
        }
        var error = await accountService.DeleteAccountAsync(User.UserId());
        if (error is not null)
        {
            Error = error;
            await OnGetAsync();
            return Page();
        }
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/Welcome?deleted=true");
    }

    private Task UpdateHouseholdAsync(Action<HouseholdInfo> change)
    {
        var id = User.HouseholdId();
        return accounts.WriteAsync(a =>
        {
            if (a.Households.FirstOrDefault(h => h.Id == id) is { } h) change(h);
        });
    }

    public record SubscriptionDto(string Endpoint, SubscriptionKeys Keys, string? Device);
    public record SubscriptionKeys([property: JsonPropertyName("p256dh")] string P256dh, string Auth);

    public async Task<IActionResult> OnPostSubscribeAsync([FromBody] SubscriptionDto sub)
    {
        if (!Uri.TryCreate(sub.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || string.IsNullOrEmpty(sub.Keys?.P256dh) || string.IsNullOrEmpty(sub.Keys.Auth)
            || sub.Endpoint.Length > 1000)
            return BadRequest();

        var userId = User.UserId();
        await store.WriteAsync(User.HouseholdId(), d =>
        {
            d.PushSubscriptions.RemoveAll(s => s.Endpoint == sub.Endpoint);
            // A phone that moved households, or a person with many old subscriptions: keep the newest 10 each.
            var mine = d.PushSubscriptions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).Skip(9).ToList();
            d.PushSubscriptions.RemoveAll(mine.Contains);
            d.PushSubscriptions.Add(new PushSubscriptionRecord
            {
                UserId = userId, Endpoint = sub.Endpoint, P256dh = sub.Keys.P256dh, Auth = sub.Keys.Auth,
                Device = sub.Device is { Length: > 60 } dev ? dev[..60] : sub.Device,
            });
        });
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostUnsubscribeAsync([FromBody] SubscriptionDto sub)
    {
        await store.WriteAsync(User.HouseholdId(), d => d.PushSubscriptions.RemoveAll(s => s.Endpoint == sub.Endpoint));
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var h = await household.GetAsync();
        var message = DailyReminderService.Compose(await planner.GetDayAsync(h.Id, clock.Today(h.TimeZone)));
        var result = await push.SendAsync(h.Id, message, User.UserId());
        return new JsonResult(result);
    }
}
