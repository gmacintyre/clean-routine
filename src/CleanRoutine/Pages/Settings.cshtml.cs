using System.Text.Json.Serialization;
using CleanRoutine.Domain;
using CleanRoutine.Push;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

public class SettingsModel(HouseholdStore store, WebPushSender push, DayPlanner planner, HouseholdClock clock,
    Microsoft.Extensions.Options.IOptions<ReminderOptions> reminderOptions)
    : PageModel
{
    /// <summary>True when the app relies on the scheduled GitHub job (Free plan), which only runs in the morning.</summary>
    public bool UsesWakeUpJob => !string.IsNullOrWhiteSpace(reminderOptions.Value.TriggerKey);

    public bool PushConfigured => push.IsConfigured;
    public string? PublicKey => push.PublicKey;
    public ReminderSettings Reminders { get; private set; } = new();
    public List<(string Name, int Devices)> People { get; private set; } = [];

    public async Task OnGetAsync()
    {
        (Reminders, People) = await store.ReadAsync(d => (
            new ReminderSettings { Enabled = d.Reminders.Enabled, Time = d.Reminders.Time },
            d.Users.Select(u => (u.DisplayName, d.PushSubscriptions.Count(s => s.UserId == u.Id))).ToList()));
    }

    public async Task<IActionResult> OnPostReminderAsync(bool enabled, TimeOnly time)
    {
        await store.WriteAsync(d =>
        {
            d.Reminders.Enabled = enabled;
            if (d.Reminders.Time != time)
            {
                d.Reminders.Time = time;
                // Moving the time later than now lets today's reminder go out at the new time.
                if (TimeOnly.FromDateTime(clock.Now) < time) d.Reminders.LastSentDate = null;
            }
        });
        TempData["Toast"] = enabled ? $"Reminders set for {time:h:mm tt}" : "Daily reminders turned off";
        return RedirectToPage();
    }

    public record SubscriptionDto(string Endpoint, SubscriptionKeys Keys, string? Device);
    public record SubscriptionKeys([property: JsonPropertyName("p256dh")] string P256dh, string Auth);

    public async Task<IActionResult> OnPostSubscribeAsync([FromBody] SubscriptionDto sub)
    {
        if (!Uri.TryCreate(sub.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || string.IsNullOrEmpty(sub.Keys?.P256dh) || string.IsNullOrEmpty(sub.Keys.Auth))
            return BadRequest();

        var userId = User.UserId();
        await store.WriteAsync(d =>
        {
            d.PushSubscriptions.RemoveAll(s => s.Endpoint == sub.Endpoint);
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
        await store.WriteAsync(d => d.PushSubscriptions.RemoveAll(s => s.Endpoint == sub.Endpoint));
        return new JsonResult(new { ok = true });
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        var message = DailyReminderService.Compose(await planner.GetDayAsync(clock.Today));
        var result = await push.SendAsync(message, User.UserId());
        return new JsonResult(result);
    }
}
