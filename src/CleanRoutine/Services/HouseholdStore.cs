using System.Collections.Concurrent;
using CleanRoutine.Domain;

namespace CleanRoutine.Services;

/// <summary>
/// Each household's chores, ticks and phones, one JSON file per household in households/{id}.json.
/// Files are loaded on first use and dropped from memory after 30 minutes without use, so memory
/// stays flat as more households sign up. A new household gets the starter routine the first time
/// its file is opened.
/// </summary>
public sealed class HouseholdStore(DataPaths paths, ILogger<HouseholdStore> log)
{
    private static readonly TimeSpan KeepCompletionsFor = TimeSpan.FromDays(3 * 365);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, JsonFile<HouseholdData>> _open = new();
    private DateTime _lastSweep = DateTime.UtcNow;

    /// <summary>Read data. Copy what you need out of <paramref name="read"/>; don't keep references.</summary>
    public Task<T> ReadAsync<T>(string householdId, Func<HouseholdData, T> read) =>
        RunAsync(householdId, read, save: false);

    public Task<T> WriteAsync<T>(string householdId, Func<HouseholdData, T> change) =>
        RunAsync(householdId, change, save: true);

    public Task WriteAsync(string householdId, Action<HouseholdData> change) =>
        WriteAsync(householdId, d =>
        {
            change(d);
            return true;
        });

    /// <summary>Delete a household's file, for when its last member deletes their account.</summary>
    public async Task DeleteAsync(string householdId)
    {
        var path = paths.HouseholdFile(householdId);
        var file = Open(householdId);
        await file.Lock.WaitAsync();
        try
        {
            file.Closed = true;
            _open.TryRemove(new KeyValuePair<string, JsonFile<HouseholdData>>(householdId, file));
            File.Delete(path);
        }
        finally { file.Lock.Release(); }
    }

    private async Task<T> RunAsync<T>(string householdId, Func<HouseholdData, T> action, bool save)
    {
        SweepIdle();
        while (true)
        {
            var file = Open(householdId);
            await file.Lock.WaitAsync();
            try
            {
                // Dropped from memory while we waited: open it again so we don't write a stale copy.
                if (file.Closed) continue;
                var data = await file.LoadAsync();
                var result = action(data);
                if (save)
                {
                    var cutoff = DateOnly.FromDateTime(DateTime.UtcNow - KeepCompletionsFor);
                    data.Completions.RemoveAll(c => c.Date < cutoff);
                    await file.SaveAsync(data);
                }
                return result;
            }
            finally { file.Lock.Release(); }
        }
    }

    private JsonFile<HouseholdData> Open(string householdId) =>
        _open.GetOrAdd(householdId, id => new JsonFile<HouseholdData>(
            paths.HouseholdFile(id), paths.BackupsDirectory, log, Normalize));

    private static bool Normalize(HouseholdData data)
    {
        var changed = false;
        if (data.Zones.Length != 7)
        {
            var zones = data.Zones;
            Array.Resize(ref zones, 7);
            data.Zones = zones;
            changed = true;
        }
        if (!data.Seeded)
        {
            SeedRoutine.Apply(data);
            changed = true;
        }
        return changed;
    }

    private void SweepIdle()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweep < TimeSpan.FromMinutes(5)) return;
        _lastSweep = now;
        foreach (var (id, file) in _open)
        {
            if (now - file.LastUsedUtc < IdleTimeout || !file.Lock.Wait(0)) continue;
            try
            {
                file.Closed = true;
                _open.TryRemove(new KeyValuePair<string, JsonFile<HouseholdData>>(id, file));
            }
            finally { file.Lock.Release(); }
        }
    }
}
