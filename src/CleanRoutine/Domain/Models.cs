namespace CleanRoutine.Domain;

public enum Frequency
{
    Daily,
    Weekly,
    Monthly,
    Seasonal,
    /// <summary>A one-off chore on a single <see cref="CleaningTask.Date"/>.</summary>
    Once,
}

/// <summary>One repeating chore. Weekly tasks use <see cref="Weekday"/>;
/// monthly and seasonal tasks use both <see cref="Weekday"/> and <see cref="WeekOfMonth"/>
/// (for example "the 2nd Saturday"); one-off tasks use <see cref="Date"/>.</summary>
public class CleaningTask
{
    public string Id { get; set; } = NewId();
    public string Title { get; set; } = "";
    public Frequency Frequency { get; set; }
    public DayOfWeek? Weekday { get; set; }
    public int? WeekOfMonth { get; set; }
    /// <summary>The day a one-off chore is due.</summary>
    public DateOnly? Date { get; set; }

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
    public string HouseholdId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Changes when the password changes, which signs the account out everywhere else.</summary>
    public string SecurityStamp { get; set; } = "";
    /// <summary>SHA-256 of the current password-reset token. The token itself is only in the email.</summary>
    public string? ResetTokenHash { get; set; }
    public DateTimeOffset? ResetTokenExpires { get; set; }

    public static string NewStamp() => Guid.NewGuid().ToString("n");
}

public enum Plan
{
    Free,
    /// <summary>Ad-free. Paid through Stripe, or given for free (<see cref="HouseholdInfo.Comped"/>).</summary>
    Premium,
}

/// <summary>A household's account-level details: kept in the accounts file so sign-in, billing and
/// reminders can find every household without opening each household's own data file.</summary>
public class HouseholdInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Name { get; set; } = "";
    /// <summary>IANA time zone. Decides what "today" is and when reminders go out.</summary>
    public string TimeZone { get; set; } = "America/New_York";
    /// <summary>Secret code in the invite link. Changing it stops old links from working.</summary>
    public string InviteCode { get; set; } = NewInviteCode();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ReminderSettings Reminders { get; set; } = new();

    public Plan Plan { get; set; } = Plan.Free;
    /// <summary>Premium without paying, such as the original household.</summary>
    public bool Comped { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    /// <summary>Stripe's subscription status, e.g. "active", "past_due", "canceled".</summary>
    public string? SubscriptionStatus { get; set; }
    /// <summary>Cancelled in Stripe but paid up until the end of the current period.</summary>
    public bool SubscriptionEnding { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAdFree => Comped || Plan == Plan.Premium;

    public static string NewInviteCode() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(9)).ToLowerInvariant();
}

/// <summary>Every account and household. Saved as accounts.json.</summary>
public class AccountsData
{
    public int SchemaVersion { get; set; } = 1;
    public List<AppUser> Users { get; set; } = [];
    public List<HouseholdInfo> Households { get; set; } = [];
    /// <summary>Recent Stripe event ids, so a webhook delivered twice is only applied once.</summary>
    public List<string> RecentStripeEvents { get; set; } = [];
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

/// <summary>One household's chores, ticks and phones. Saved as households/{id}.json.</summary>
public class HouseholdData
{
    public int SchemaVersion { get; set; } = 2;
    public bool Seeded { get; set; }
    public List<CleaningTask> Tasks { get; set; } = [];
    /// <summary>Zone name per day, indexed by <see cref="DayOfWeek"/> (0 = Sunday).</summary>
    public string[] Zones { get; set; } = new string[7];
    public List<Completion> Completions { get; set; } = [];
    public List<PushSubscriptionRecord> PushSubscriptions { get; set; } = [];
}

/// <summary>The single-file format used before the app was opened up (schema 1). Read once, to move
/// the original household into the new layout.</summary>
public class LegacyHouseholdData
{
    public bool Seeded { get; set; }
    public List<AppUser> Users { get; set; } = [];
    public List<CleaningTask> Tasks { get; set; } = [];
    public string[] Zones { get; set; } = new string[7];
    public List<Completion> Completions { get; set; } = [];
    public List<PushSubscriptionRecord> PushSubscriptions { get; set; } = [];
    public ReminderSettings Reminders { get; set; } = new();
}
