using System.Globalization;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

public class IndexModel(DayPlanner planner, HouseholdClock clock, AccountStore accounts, CurrentHousehold household,
    TimeProvider time) : PageModel
{
    public DayPlan Plan { get; private set; } = null!;
    public DateOnly Today { get; private set; }
    /// <summary>User ids in sign-up order, so each person keeps the same badge color.</summary>
    public List<string> UserOrder { get; private set; } = [];

    public async Task OnGetAsync(string? date)
    {
        var h = await household.GetAsync();
        Today = clock.Today(h.TimeZone);
        var day = TryParse(date) ?? Today;
        Plan = await planner.GetDayAsync(h.Id, day);
        UserOrder = await accounts.ReadAsync(d => d.Users.Where(u => u.HouseholdId == h.Id).Select(u => u.Id).ToList());
    }

    public async Task<IActionResult> OnPostToggleAsync(string taskId, string date)
    {
        if (TryParse(date) is not { } day) return BadRequest();
        var hid = User.HouseholdId();
        var done = await planner.ToggleAsync(hid, taskId, day, User.UserId(), time.GetUtcNow());
        if (done is null) return NotFound();
        var plan = await planner.GetDayAsync(hid, day);
        return new JsonResult(new { done, doneCount = plan.DoneCount, total = plan.Tasks.Count });
    }

    public static string Key(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? TryParse(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
