using CleanRoutine.Domain;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

/// <summary>All chores, grouped by how often they happen. Chores are added and edited on the Chore page;
/// day zones are renamed here.</summary>
public class RoutineModel(HouseholdStore store, HouseholdClock clock, CurrentHousehold household) : PageModel
{
    public List<CleaningTask> Tasks { get; private set; } = [];
    public string[] Zones { get; private set; } = new string[7];
    public DateOnly Today { get; private set; }

    public IEnumerable<CleaningTask> Of(Frequency f) => Tasks.Where(t => t.Frequency == f);

    /// <summary>One-off chores from today on, soonest first. Past ones stay out of the way.</summary>
    public IEnumerable<CleaningTask> UpcomingOnce =>
        Of(Frequency.Once).Where(t => t.Date >= Today).OrderBy(t => t.Date);

    public async Task OnGetAsync()
    {
        var h = await household.GetAsync();
        Today = clock.Today(h.TimeZone);
        (Tasks, Zones) = await store.ReadAsync(h.Id, d => (
            d.Tasks.Select(t => new CleaningTask
            {
                Id = t.Id, Title = t.Title, Frequency = t.Frequency, Weekday = t.Weekday,
                WeekOfMonth = t.WeekOfMonth, Date = t.Date,
            }).ToList(),
            (string[])d.Zones.Clone()));
    }

    public async Task<IActionResult> OnPostZoneAsync(DayOfWeek day, string? name)
    {
        if (!Enum.IsDefined(day)) return BadRequest();
        name = (name ?? "").Trim();
        await store.WriteAsync(User.HouseholdId(), d => d.Zones[(int)day] = name.Length > 40 ? name[..40] : name);
        TempData["Toast"] = name.Length > 0 ? $"{day} is now {name} day" : $"Cleared {day}'s name";
        return Redirect($"/Routine#day-{(int)day}");
    }

    /// <summary>Where on this page a chore is listed.</summary>
    public static string Anchor(CleaningTask t) => t.Frequency switch
    {
        Frequency.Daily => "/Routine#daily",
        Frequency.Weekly => $"/Routine#day-{(int)(t.Weekday ?? DayOfWeek.Monday)}",
        Frequency.Monthly => "/Routine#monthly",
        Frequency.Seasonal => "/Routine#seasonal",
        _ => "/Routine#once",
    };
}
