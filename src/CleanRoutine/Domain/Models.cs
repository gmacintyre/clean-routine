namespace CleanRoutine.Domain;

public enum Frequency
{
    Daily,
    Weekly,
    Monthly,
    Seasonal,
}

/// <summary>One repeating chore. Weekly tasks use <see cref="Weekday"/>;
/// monthly and seasonal tasks use both <see cref="Weekday"/> and <see cref="WeekOfMonth"/>
/// (for example "the 2nd Saturday").</summary>
public class CleaningTask
{
    public string Id { get; set; } = NewId();
    public string Title { get; set; } = "";
    public Frequency Frequency { get; set; }
    public DayOfWeek? Weekday { get; set; }
    public int? WeekOfMonth { get; set; }

    public static string NewId() => Guid.NewGuid().ToString("n")[..12];
}

public class Completion
{
    public string TaskId { get; set; } = "";
    public DateOnly Date { get; set; }
    public string UserId { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PushSubscriptionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string UserId { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? Device { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ReminderSettings
{
    public bool Enabled { get; set; } = true;
    public TimeOnly Time { get; set; } = new(7, 30);
    public DateOnly? LastSentDate { get; set; }
}

/// <summary>Everything the household app stores. Saved as one JSON file.</summary>
public class HouseholdData
{
    public int SchemaVersion { get; set; } = 1;
    public bool Seeded { get; set; }
    public List<AppUser> Users { get; set; } = [];
    public List<CleaningTask> Tasks { get; set; } = [];
    /// <summary>Zone name per day, indexed by <see cref="DayOfWeek"/> (0 = Sunday).</summary>
    public string[] Zones { get; set; } = new string[7];
    public List<Completion> Completions { get; set; } = [];
    public List<PushSubscriptionRecord> PushSubscriptions { get; set; } = [];
    public ReminderSettings Reminders { get; set; } = new();
}
