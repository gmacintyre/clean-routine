using System.Globalization;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

public class CalendarModel(DayPlanner planner, HouseholdClock clock) : PageModel
{
    public DateOnly Month { get; private set; }
    public DateOnly Today { get; private set; }
    public IReadOnlyList<CalendarDay> Days { get; private set; } = [];

    public async Task OnGetAsync(string? month)
    {
        Today = clock.Today;
        Month = DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? m
            : new DateOnly(Today.Year, Today.Month, 1);

        // Sunday-first grid covering the whole month.
        var start = Month.AddDays(-(int)Month.DayOfWeek);
        var last = Month.AddMonths(1).AddDays(-1);
        var end = last.AddDays(6 - (int)last.DayOfWeek);
        Days = await planner.GetRangeAsync(start, end);
    }

    public static string Key(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
