using System.Globalization;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

public class IndexModel(DayPlanner planner, HouseholdClock clock, HouseholdStore store, TimeProvider time) : PageModel
{
    public DayPlan Plan { get; private set; } = null!;
    public DateOnly Today { get; private set; }
    /// <summary>User ids in sign-up order, so each person keeps the same badge color.</summary>
    public List<string> UserOrder { get; private set; } = [];

    public async Task OnGetAsync(string? date)
    {
        Today = clock.Today;
        var day = TryParse(date) ?? Today;
        Plan = await planner.GetDayAsync(day);
        UserOrder = await store.ReadAsync(d => d.Users.Select(u => u.Id).ToList());
    }

    public async Task<IActionResult> OnPostToggleAsync(string taskId, string date)
    {
        if (TryParse(date) is not { } day) return BadRequest();
        var done = await planner.ToggleAsync(taskId, day, User.UserId(), time.GetUtcNow());
        if (done is null) return NotFound();
        var plan = await planner.GetDayAsync(day);
        return new JsonResult(new { done, doneCount = plan.DoneCount, total = plan.Tasks.Count });
    }

    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? TryParse(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
