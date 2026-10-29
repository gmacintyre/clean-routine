using CleanRoutine.Domain;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

public class RoutineModel(HouseholdStore store) : PageModel
{
    public List<CleaningTask> Tasks { get; private set; } = [];
    public string[] Zones { get; private set; } = new string[7];

    public async Task OnGetAsync()
    {
        (Tasks, Zones) = await store.ReadAsync(d => (
            d.Tasks.Select(t => new CleaningTask
            {
                Id = t.Id, Title = t.Title, Frequency = t.Frequency, Weekday = t.Weekday, WeekOfMonth = t.WeekOfMonth,
            }).ToList(),
            (string[])d.Zones.Clone()));
    }

    public async Task<IActionResult> OnPostAddAsync(Frequency frequency, DayOfWeek? weekday, string? title)
    {
        title = title?.Trim();
        if (string.IsNullOrEmpty(title)) return Back(frequency, weekday);

        var task = new CleaningTask { Title = Limit(title), Frequency = frequency };
        if (frequency == Frequency.Weekly) task.Weekday = weekday ?? DayOfWeek.Monday;
        if (frequency is Frequency.Monthly or Frequency.Seasonal)
        {
            task.Weekday = DayOfWeek.Saturday;
            task.WeekOfMonth = 1;
        }
        await store.WriteAsync(d => d.Tasks.Add(task));
        return Back(frequency, weekday);
    }

    public async Task<IActionResult> OnPostUpdateAsync(string id, string? title, DayOfWeek? weekday, int? week)
    {
        var (freq, day) = await store.WriteAsync(d =>
        {
            var t = d.Tasks.FirstOrDefault(x => x.Id == id);
            if (t is null) return ((Frequency?)null, (DayOfWeek?)null);
            if (!string.IsNullOrWhiteSpace(title)) t.Title = Limit(title.Trim());
            if (t.Frequency is Frequency.Monthly or Frequency.Seasonal)
            {
                if (weekday is not null) t.Weekday = weekday;
                if (week is >= 1 and <= 4) t.WeekOfMonth = week;
            }
            return (t.Frequency, t.Weekday);
        });
        return freq is null ? RedirectToPage() : Back(freq.Value, day);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var removed = await store.WriteAsync(d =>
        {
            var t = d.Tasks.FirstOrDefault(x => x.Id == id);
            if (t is not null) d.Tasks.Remove(t);
            return t;
        });
        if (removed is null) return RedirectToPage();
        TempData["Toast"] = $"Removed “{removed.Title}”";
        return Back(removed.Frequency, removed.Weekday);
    }

    public async Task<IActionResult> OnPostZoneAsync(DayOfWeek day, string? name)
    {
        await store.WriteAsync(d => d.Zones[(int)day] = Limit(name?.Trim() ?? "", 40));
        return Redirect($"/Routine#day-{(int)day}");
    }

    private RedirectResult Back(Frequency f, DayOfWeek? day) => Redirect(f switch
    {
        Frequency.Daily => "/Routine#daily",
        Frequency.Weekly => $"/Routine#day-{(int)(day ?? DayOfWeek.Monday)}",
        Frequency.Monthly => "/Routine#monthly",
        _ => "/Routine#seasonal",
    });

    private static string Limit(string s, int max = 120) => s.Length > max ? s[..max] : s;
}
