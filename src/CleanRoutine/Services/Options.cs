namespace CleanRoutine.Services;

public class HouseholdOptions
{
    /// <summary>IANA time zone the household lives in. Decides what "today" is and when reminders go out.</summary>
    public string TimeZone { get; set; } = "America/New_York";

    /// <summary>The only email addresses allowed to create an account.</summary>
    public string[] AllowedEmails { get; set; } = [];
}

public class StorageOptions
{
    /// <summary>Folder for the data file. Leave empty to use %HOME%/data/clean-routine on Azure
    /// App Service (persistent storage) or ./App_Data when running locally.</summary>
    public string? DataDirectory { get; set; }
}

public class PushOptions
{
    /// <summary>VAPID public key (base64url). Generate with: dotnet run -- generate-vapid-keys</summary>
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    /// <summary>Contact for push services, e.g. "mailto:you@example.com".</summary>
    public string Subject { get; set; } = "mailto:admin@example.com";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

public class ReminderOptions
{
    /// <summary>Shared secret the scheduled GitHub job sends in the X-Reminder-Key header to wake the
    /// app and send the daily reminder. Leave empty to turn the wake-up link off.</summary>
    public string? TriggerKey { get; set; }
}
