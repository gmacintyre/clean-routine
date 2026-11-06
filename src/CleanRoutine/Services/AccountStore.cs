using CleanRoutine.Domain;

namespace CleanRoutine.Services;

/// <summary>Every account and household's account-level details, in accounts.json. Small (a few hundred
/// bytes per person), so it stays in memory. Chores and ticks live in each household's own file
/// (<see cref="HouseholdStore"/>).</summary>
public sealed class AccountStore(DataPaths paths, ILogger<AccountStore> log)
{
    private readonly JsonFile<AccountsData> _file = new(paths.AccountsFile, paths.BackupsDirectory, log, GiveStamps);

    /// <summary>Accounts from before security stamps existed get one, saved once so it stays the same.</summary>
    private static bool GiveStamps(AccountsData data)
    {
        var missing = data.Users.Where(u => string.IsNullOrEmpty(u.SecurityStamp)).ToList();
        foreach (var u in missing) u.SecurityStamp = AppUser.NewStamp();
        return missing.Count > 0;
    }

    public bool Exists => File.Exists(paths.AccountsFile);

    /// <summary>Read data. Copy what you need out of <paramref name="read"/>; don't keep references.</summary>
    public Task<T> ReadAsync<T>(Func<AccountsData, T> read) => _file.ReadAsync(read);

    public Task<T> WriteAsync<T>(Func<AccountsData, T> change) => _file.WriteAsync(change);

    public Task WriteAsync(Action<AccountsData> change) => _file.WriteAsync(d =>
    {
        change(d);
        return true;
    });

    /// <summary>A copy of one household's details, or null.</summary>
    public Task<HouseholdInfo?> GetHouseholdAsync(string householdId) =>
        ReadAsync(d => d.Households.FirstOrDefault(h => h.Id == householdId) is { } h ? Copy(h) : null);

    public static HouseholdInfo Copy(HouseholdInfo h) => new()
    {
        Id = h.Id, Name = h.Name, TimeZone = h.TimeZone, InviteCode = h.InviteCode, CreatedAt = h.CreatedAt,
        Reminders = new ReminderSettings { Enabled = h.Reminders.Enabled, Time = h.Reminders.Time, LastSentDate = h.Reminders.LastSentDate },
        Plan = h.Plan, Comped = h.Comped, StripeCustomerId = h.StripeCustomerId,
        StripeSubscriptionId = h.StripeSubscriptionId, SubscriptionStatus = h.SubscriptionStatus,
        SubscriptionEnding = h.SubscriptionEnding,
    };
}
