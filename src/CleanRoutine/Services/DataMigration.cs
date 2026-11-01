using System.Text.Json;
using CleanRoutine.Domain;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Services;

/// <summary>
/// Moves the original single-file household (household.json) into the multi-household layout the
/// first time this version starts: accounts.json for the two accounts, and households/{id}.json for the
/// chores and ticks. Nothing is lost: the old file is kept, renamed to household.json.migrated.
/// The original household is marked ad-free for good.
/// </summary>
public static class DataMigration
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var paths = services.GetRequiredService<DataPaths>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DataMigration));
        if (File.Exists(paths.AccountsFile) || !File.Exists(paths.LegacyHouseholdFile)) return;

        LegacyHouseholdData legacy;
        await using (var stream = File.OpenRead(paths.LegacyHouseholdFile))
        {
            legacy = await JsonSerializer.DeserializeAsync<LegacyHouseholdData>(stream, StoreJson.Options)
                     ?? throw new InvalidDataException("household.json is empty or not valid.");
        }

        var defaults = services.GetRequiredService<IOptions<HouseholdOptions>>().Value;
        var household = new HouseholdInfo
        {
            Name = "Our home",
            TimeZone = defaults.TimeZone,
            Reminders = legacy.Reminders,
            Plan = Plan.Premium,
            Comped = true,
        };
        foreach (var u in legacy.Users) u.HouseholdId = household.Id;

        var data = new HouseholdData
        {
            Seeded = legacy.Seeded,
            Tasks = legacy.Tasks,
            Zones = legacy.Zones,
            Completions = legacy.Completions,
            PushSubscriptions = legacy.PushSubscriptions,
        };

        // Household file first, accounts file last: accounts.json existing is what marks the move as done.
        await WriteAsync(paths.HouseholdFile(household.Id), data);
        await WriteAsync(paths.AccountsFile, new AccountsData { Users = legacy.Users, Households = [household] });
        File.Move(paths.LegacyHouseholdFile, paths.LegacyHouseholdFile + ".migrated", overwrite: true);

        log.LogInformation("Moved the original household ({Users} accounts, {Tasks} chores) to the multi-household layout.",
            legacy.Users.Count, legacy.Tasks.Count);
    }

    private static async Task WriteAsync<T>(string path, T value)
    {
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, value, StoreJson.Options);
        }
        File.Move(temp, path, overwrite: true);
    }
}
