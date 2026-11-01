using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace CleanRoutine.Services;

/// <summary>Gives the current date and time in a household's time zone, not the server's
/// (Azure servers run on UTC).</summary>
public class HouseholdClock(IOptions<HouseholdOptions> options, TimeProvider time, ILogger<HouseholdClock> log)
{
    private readonly ConcurrentDictionary<string, TimeZoneInfo> _zones = new();

    public DateTime Now(string? timeZone) => TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone(timeZone)).DateTime;
    public DateOnly Today(string? timeZone) => DateOnly.FromDateTime(Now(timeZone));

    public static bool IsValidZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    private TimeZoneInfo Zone(string? id)
    {
        id = string.IsNullOrWhiteSpace(id) ? options.Value.TimeZone : id;
        return _zones.GetOrAdd(id, z =>
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(z, out var found)) return found;
            log.LogWarning("Time zone {Zone} not found; using UTC.", z);
            return TimeZoneInfo.Utc;
        });
    }
}
