using System.Globalization;
using CleanRoutine.Domain;
using CleanRoutine.Services;

namespace CleanRoutine.Push;

/// <summary>
/// Checks once a minute and sends the day's reminder once the household's reminder time has passed.
/// Needs the App Service "Always On" setting so the app isn't asleep at that time.
/// </summary>
public class DailyReminderService(
    HouseholdStore store,
    HouseholdClock clock,
    DayPlanner planner,
    WebPushSender sender,
    ILogger<DailyReminderService> log) : BackgroundService
{
    /// <summary>If the app was down at reminder time, still send until this hour, then skip the day.</summary>
    private const int LatestCatchUpHour = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sender.IsConfigured)
        {
            log.LogWarning("Push keys are not set (Push:PublicKey / Push:PrivateKey); daily reminders are off.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Daily reminder check failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = clock.Now;
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);

        // Claim today's reminder before sending, so a slow send can't cause a second one.
        var shouldSend = await store.WriteAsync(d =>
        {
            var r = d.Reminders;
            if (!r.Enabled || r.LastSentDate == today || time < r.Time || now.Hour > LatestCatchUpHour) return false;
            if (d.PushSubscriptions.Count == 0) return false;
            r.LastSentDate = today;
            return true;
        });
        if (!shouldSend) return;

        var plan = await planner.GetDayAsync(today);
        var result = await sender.SendAsync(Compose(plan), ct: ct);
        log.LogInformation("Daily reminder for {Date}: {Sent} sent, {Removed} removed, {Failed} failed.",
            today, result.Sent, result.Removed, result.Failed);
    }

    public static PushMessage Compose(DayPlan plan)
    {
        var dayName = plan.Date.ToString("dddd", CultureInfo.InvariantCulture);
        var title = string.IsNullOrWhiteSpace(plan.Zone) ? $"{dayName}'s cleaning" : $"{dayName}: {plan.Zone} day";

        var daily = plan.Tasks.Count(t => t.Frequency == Frequency.Daily);
        var big = plan.Tasks.Where(t => t.Frequency is Frequency.Monthly or Frequency.Seasonal)
            .Select(t => t.Title).ToList();
        var weekly = plan.Tasks.Where(t => t.Frequency == Frequency.Weekly).Select(t => t.Title).ToList();

        var parts = new List<string>();
        if (big.Count > 0) parts.Add("Bigger job: " + string.Join("; ", big));
        if (weekly.Count > 0) parts.Add(string.Join(", ", weekly));
        if (daily > 0) parts.Add($"Plus {daily} daily basics");
        var body = parts.Count > 0 ? string.Join(". ", parts) : "Nothing scheduled today.";

        return new PushMessage(title, body, "/", "daily");
    }
}
