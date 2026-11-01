using CleanRoutine.Domain;

namespace CleanRoutine.Services;

public record PlannedTask(string Id, string Title, Frequency Frequency, int? WeekOfMonth, DayOfWeek? Weekday,
    string? DoneByUserId, string? DoneByName);

public record DayPlan(DateOnly Date, string? Zone, IReadOnlyList<PlannedTask> Tasks)
{
    public int DoneCount => Tasks.Count(t => t.DoneByUserId is not null);
    public bool HasBigJob => Tasks.Any(t => t.Frequency is Frequency.Monthly or Frequency.Seasonal);
}

public record CalendarDay(DateOnly Date, string? Zone, int Due, int Done, bool HasBigJob);

/// <summary>Works out which tasks fall on which day, and what has been checked off.</summary>
public class DayPlanner(HouseholdStore store, AccountStore accounts)
{
    /// <summary>Display names by user id, for everyone in the household.</summary>
    public Task<Dictionary<string, string>> NamesAsync(string householdId) => accounts.ReadAsync(a =>
        a.Users.Where(u => u.HouseholdId == householdId).ToDictionary(u => u.Id, u => u.DisplayName));

    public async Task<DayPlan> GetDayAsync(string householdId, DateOnly date)
    {
        var names = await NamesAsync(householdId);
        return await store.ReadAsync(householdId, d => Plan(d, date, names));
    }

    private static DayPlan Plan(HouseholdData d, DateOnly date, Dictionary<string, string> names)
    {
        var done = d.Completions.Where(c => c.Date == date).ToDictionary(c => c.TaskId, c => c.UserId);
        var tasks = d.Tasks.Where(t => Schedule.IsDue(t, date))
            .Select(t =>
            {
                var by = done.GetValueOrDefault(t.Id);
                return new PlannedTask(t.Id, t.Title, t.Frequency, t.WeekOfMonth, t.Weekday, by,
                    by is null ? null : names.GetValueOrDefault(by));
            })
            .ToList();
        return new DayPlan(date, d.Zones[(int)date.DayOfWeek], tasks);
    }

    public Task<IReadOnlyList<CalendarDay>> GetRangeAsync(string householdId, DateOnly from, DateOnly to) =>
        store.ReadAsync(householdId, d =>
    {
        var doneByDate = d.Completions.Where(c => c.Date >= from && c.Date <= to)
            .GroupBy(c => c.Date)
            .ToDictionary(g => g.Key, g => g.Select(c => c.TaskId).ToHashSet());
        var days = new List<CalendarDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var due = d.Tasks.Where(t => Schedule.IsDue(t, date)).ToList();
            var done = doneByDate.GetValueOrDefault(date);
            days.Add(new CalendarDay(date, d.Zones[(int)date.DayOfWeek], due.Count,
                done is null ? 0 : due.Count(t => done.Contains(t.Id)), due.Any(Schedule.IsBigJob)));
        }
        return (IReadOnlyList<CalendarDay>)days;
    });

    /// <summary>Check a task off, or un-check it. Returns true when it ends up done.</summary>
    public Task<bool?> ToggleAsync(string householdId, string taskId, DateOnly date, string userId, DateTimeOffset now) =>
        store.WriteAsync<bool?>(householdId, d =>
        {
            var task = d.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is null || !Schedule.IsDue(task, date)) return null;

            var existing = d.Completions.FirstOrDefault(c => c.TaskId == taskId && c.Date == date);
            if (existing is not null)
            {
                d.Completions.Remove(existing);
                return false;
            }

            d.Completions.Add(new Completion { TaskId = taskId, Date = date, UserId = userId, At = now });
            return true;
        });
}
