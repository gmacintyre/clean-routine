namespace CleanRoutine.Domain;

public static class Schedule
{
    /// <summary>Seasonal tasks run in January, April, July and October.</summary>
    public static readonly int[] SeasonalMonths = [1, 4, 7, 10];

    /// <summary>1 for days 1-7, 2 for 8-14, 3 for 15-21, 4 for 22-28, 5 after that.</summary>
    public static int WeekOfMonth(DateOnly date) => (date.Day - 1) / 7 + 1;

    public static bool IsDue(CleaningTask task, DateOnly date) => task.Frequency switch
    {
        Frequency.Daily => true,
        Frequency.Weekly => task.Weekday == date.DayOfWeek,
        Frequency.Monthly => task.Weekday == date.DayOfWeek && task.WeekOfMonth == WeekOfMonth(date),
        Frequency.Seasonal => SeasonalMonths.Contains(date.Month)
                              && task.Weekday == date.DayOfWeek
                              && task.WeekOfMonth == WeekOfMonth(date),
        Frequency.Once => task.Date == date,
        _ => false,
    };

    public static bool IsBigJob(CleaningTask task) =>
        task.Frequency is Frequency.Monthly or Frequency.Seasonal;

    /// <summary>How the task repeats, in words: "Every Thursday", "2nd Saturday of each month".</summary>
    public static string Describe(CleaningTask t) => t.Frequency switch
    {
        Frequency.Daily => "Every day",
        Frequency.Weekly => $"Every {t.Weekday}",
        Frequency.Monthly => $"{Ordinal(t.WeekOfMonth ?? 1)} {t.Weekday} of each month",
        Frequency.Seasonal => $"{Ordinal(t.WeekOfMonth ?? 1)} {t.Weekday} in Jan, Apr, Jul and Oct",
        Frequency.Once => t.Date is { } d ? $"Just once, {d:ddd MMM d}" : "Just once",
        _ => "",
    };

    public static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };

    /// <summary>Monday-first order for showing the week.</summary>
    public static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];
}
