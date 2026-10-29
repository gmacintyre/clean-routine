using System.Text.Json;
using System.Text.Json.Serialization;
using CleanRoutine.Domain;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Services;

/// <summary>
/// Keeps all household data in one JSON file. Every read and write goes through a lock, so the
/// two of you checking things off at the same moment can't overwrite each other. Writes go to a
/// temp file first and are then swapped in, and a dated backup is kept for the last 14 days.
/// </summary>
public sealed class HouseholdStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const int BackupsToKeep = 14;
    private static readonly TimeSpan KeepCompletionsFor = TimeSpan.FromDays(3 * 365);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<HouseholdStore> _log;
    private HouseholdData? _data;

    public string FilePath { get; }
    private string BackupDirectory { get; }

    public HouseholdStore(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<HouseholdStore> log)
    {
        _log = log;
        var dir = ResolveDirectory(options.Value.DataDirectory, env);
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "household.json");
        BackupDirectory = Path.Combine(dir, "backups");
        _log.LogInformation("Household data file: {Path}", FilePath);
    }

    private static string ResolveDirectory(string? configured, IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        // On Azure App Service, %HOME% is persistent storage shared across restarts and deploys.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null && !string.IsNullOrEmpty(home))
            return Path.Combine(home, "data", "clean-routine");

        return Path.Combine(env.ContentRootPath, "App_Data");
    }

    /// <summary>Read data. Copy what you need out of <paramref name="read"/>; don't keep references.</summary>
    public async Task<T> ReadAsync<T>(Func<HouseholdData, T> read)
    {
        await _lock.WaitAsync();
        try
        {
            return read(await LoadAsync());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<HouseholdData, T> change)
    {
        await _lock.WaitAsync();
        try
        {
            var data = await LoadAsync();
            var result = change(data);
            await SaveAsync(data);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task WriteAsync(Action<HouseholdData> change) =>
        WriteAsync(d =>
        {
            change(d);
            return true;
        });

    private async Task<HouseholdData> LoadAsync()
    {
        if (_data is not null) return _data;

        if (File.Exists(FilePath))
        {
            await using var stream = File.OpenRead(FilePath);
            _data = await JsonSerializer.DeserializeAsync<HouseholdData>(stream, Json)
                    ?? throw new InvalidDataException($"{FilePath} is empty or not valid.");
        }
        else
        {
            _data = new HouseholdData();
        }

        if (_data.Zones.Length != 7)
        {
            var zones = _data.Zones;
            Array.Resize(ref zones, 7);
            _data.Zones = zones;
        }

        if (!_data.Seeded)
        {
            SeedRoutine.Apply(_data);
            await SaveAsync(_data);
        }

        return _data;
    }

    private async Task SaveAsync(HouseholdData data)
    {
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow - KeepCompletionsFor);
        data.Completions.RemoveAll(c => c.Date < cutoff);

        BackupOncePerDay();

        var temp = FilePath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, data, Json);
        }
        File.Move(temp, FilePath, overwrite: true);
    }

    private void BackupOncePerDay()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            Directory.CreateDirectory(BackupDirectory);
            var target = Path.Combine(BackupDirectory, $"household-{DateTime.UtcNow:yyyy-MM-dd}.json");
            if (File.Exists(target)) return;
            File.Copy(FilePath, target);
            foreach (var old in Directory.GetFiles(BackupDirectory, "household-*.json")
                         .OrderDescending().Skip(BackupsToKeep))
                File.Delete(old);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write the daily backup.");
        }
    }
}
