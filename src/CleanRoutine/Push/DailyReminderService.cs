using System.Globalization;
using CleanRoutine.Domain;
using CleanRoutine.Services;

namespace CleanRoutine.Push;

/// <summary>
/// Sends each household its day's reminder once that household's reminder time has passed (in its own
/// time zone), at most once a day. While the app is awake it checks every minute. On a plan without
/// Always On the app sleeps when idle, so a scheduled GitHub Actions job also calls POST /api/reminders/run
/// to wake it (see Program.cs).
/// </summary>
public class DailyReminderService(
    AccountStore accounts,
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
                await RunIfDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Daily reminder check failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Send every reminder that's due and hasn't gone out yet. Safe to call any number of times.</summary>
    /// <returns>What happened, as a short status.</returns>
    public async Task<string> RunIfDueAsync(CancellationToken ct = default)
    {
        if (!sender.IsConfigured) return "push-not-configured";

        // Claim each due reminder before sending, so two callers at once (the timer and the wake-up job)
        // can't both send it. The store's lock makes this check-and-set atomic.
        // Look first without writing, so a minute with nothing due doesn't rewrite the accounts file.
        if (!await accounts.ReadAsync(a => a.Households.Any(h => IsDue(h, out _)))) return "nothing-due";
        var due = await accounts.WriteAsync(a =>
        {
            var list = new List<(string Id, DateOnly Today)>();
            foreach (var h in a.Households)
            {
                if (!IsDue(h, out var today)) continue;
                h.Reminders.LastSentDate = today;
                list.Add((h.Id, today));
            }
            return list;
        });

        var sent = 0;
        foreach (var (id, today) in due)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!await store.ReadAsync(id, d => d.PushSubscriptions.Count > 0)) continue;
                var result = await sender.SendAsync(id, Compose(await planner.GetDayAsync(id, today)), ct: ct);
                if (result.Sent > 0) sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Reminder for household {Id} failed.", id);
            }
        }
        if (due.Count > 0) log.LogInformation("Daily reminders: {Due} due, {Sent} households reached.", due.Count, sent);
        return due.Count == 0 ? "nothing-due" : $"sent-{sent}-of-{due.Count}";
    }

    private bool IsDue(HouseholdInfo h, out DateOnly today)
    {
        var now = clock.Now(h.TimeZone);
        today = DateOnly.FromDateTime(now);
        var r = h.Reminders;
        return r.Enabled && r.LastSentDate != today && TimeOnly.FromDateTime(now) >= r.Time && now.Hour <= LatestCatchUpHour;
    }

    public static PushMessage Compose(DayPlan plan)
    {
        var dayName = plan.Date.ToString("dddd", CultureInfo.InvariantCulture);
        var title = string.IsNullOrWhiteSpace(plan.Zone) ? $"{dayName}'s cleaning" : $"{dayName}: {plan.Zone} day";

        var daily = plan.Tasks.Count(t => t.Frequency == Frequency.Daily);
        var big = plan.Tasks.Where(t => t.Frequency is Frequency.Monthly or Frequency.Seasonal)
            .Select(t => t.Title).ToList();
        var weekly = plan.Tasks.Where(t => t.Frequency is Frequency.Weekly or Frequency.Once).Select(t => t.Title).ToList();

        var parts = new List<string>();
        if (big.Count > 0) parts.Add("Bigger job: " + string.Join("; ", big));
        if (weekly.Count > 0) parts.Add(string.Join(", ", weekly));
        if (daily > 0) parts.Add($"Plus {daily} daily basics");
        var body = parts.Count > 0 ? string.Join(". ", parts) : "Nothing scheduled today.";

        return new PushMessage(title, body, "/", "daily");
    }
}
