using System.Globalization;
using CleanRoutine.Domain;
using CleanRoutine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CleanRoutine.Pages;

/// <summary>Add a new chore or edit one: its name, how often it repeats, and which day it lands on.</summary>
public class ChoreModel(HouseholdStore store, HouseholdClock clock) : PageModel
{
    public const int MaxTitle = 120;

    public class ChoreInput
    {
        public string Title { get; set; } = "";
        public Frequency Frequency { get; set; } = Frequency.Weekly;
        public DayOfWeek Weekday { get; set; } = DayOfWeek.Monday;
        public int WeekOfMonth { get; set; } = 1;
        public DateOnly? Date { get; set; }
    }

    [BindProperty] public ChoreInput Input { get; set; } = new();
    [FromRoute] public string? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public bool IsNew => Id is null;
    public List<string> Errors { get; } = [];
    public DateOnly Today { get; private set; }

    public async Task<IActionResult> OnGetAsync(Frequency? frequency, DayOfWeek? weekday, string? date)
    {
        Today = clock.Today;
        if (IsNew)
        {
            if (frequency is not null) Input.Frequency = frequency.Value;
            if (weekday is not null) Input.Weekday = weekday.Value;
            if (frequency is Frequency.Monthly or Frequency.Seasonal && weekday is null) Input.Weekday = DayOfWeek.Saturday;
            Input.Date = ParseDate(date) ?? Today;
            return Page();
        }

        var task = await store.ReadAsync(d => d.Tasks.FirstOrDefault(t => t.Id == Id) is { } t
            ? new ChoreInput
            {
                Title = t.Title,
                Frequency = t.Frequency,
                Weekday = t.Weekday ?? DayOfWeek.Monday,
                WeekOfMonth = t.WeekOfMonth ?? 1,
                Date = t.Date,
            }
            : null);
        if (task is null) return NotFound();
        Input = task;
        Input.Date ??= Today;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Today = clock.Today;
        var title = (Input.Title ?? "").Trim();
        if (title.Length == 0) Errors.Add("Give the chore a name.");
        if (title.Length > MaxTitle) Errors.Add($"Keep the name under {MaxTitle} characters.");
        if (!Enum.IsDefined(Input.Frequency)) Errors.Add("Choose how often it happens.");
        if (!Enum.IsDefined(Input.Weekday)) Errors.Add("Choose a day of the week.");
        if (Input.Frequency is Frequency.Monthly or Frequency.Seasonal && Input.WeekOfMonth is < 1 or > 4)
            Errors.Add("Choose the 1st, 2nd, 3rd or 4th week.");
        if (Input.Frequency == Frequency.Once && Input.Date is null) Errors.Add("Choose the date it's due.");
        if (Errors.Count > 0) return Page();

        var saved = await store.WriteAsync(d =>
        {
            CleaningTask? task;
            if (IsNew)
            {
                task = new CleaningTask();
                d.Tasks.Add(task);
            }
            else
            {
                task = d.Tasks.FirstOrDefault(t => t.Id == Id);
                if (task is null) return null;
            }

            task.Title = title;
            task.Frequency = Input.Frequency;
            task.Weekday = Input.Frequency is Frequency.Daily or Frequency.Once ? null : Input.Weekday;
            task.WeekOfMonth = Input.Frequency is Frequency.Monthly or Frequency.Seasonal ? Input.WeekOfMonth : null;
            task.Date = Input.Frequency == Frequency.Once ? Input.Date : null;
            return new CleaningTask
            {
                Id = task.Id, Title = task.Title, Frequency = task.Frequency, Weekday = task.Weekday,
                WeekOfMonth = task.WeekOfMonth, Date = task.Date,
            };
        });
        if (saved is null) return NotFound();

        TempData["Toast"] = IsNew ? $"Added “{saved.Title}”" : $"Saved “{saved.Title}”";
        return Redirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : RoutineModel.Anchor(saved));
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var removed = await store.WriteAsync(d =>
        {
            var task = d.Tasks.FirstOrDefault(t => t.Id == Id);
            if (task is null) return null;
            d.Tasks.Remove(task);
            // Ticks for this chore no longer belong to anything.
            d.Completions.RemoveAll(c => c.TaskId == task.Id);
            return task;
        });
        if (removed is null) return NotFound();
        TempData["Toast"] = $"Deleted “{removed.Title}”";
        return Redirect(RoutineModel.Anchor(removed));
    }

    public string CancelUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Routine";

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
