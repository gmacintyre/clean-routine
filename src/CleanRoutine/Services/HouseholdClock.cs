using Microsoft.Extensions.Options;

namespace CleanRoutine.Services;

/// <summary>Gives the current date and time in the household's time zone, not the server's
/// (Azure servers run on UTC).</summary>
public class HouseholdClock
{
    private readonly TimeZoneInfo _zone;
    private readonly TimeProvider _time;

    public HouseholdClock(IOptions<HouseholdOptions> options, TimeProvider time, ILogger<HouseholdClock> log)
    {
        _time = time;
        try
        {
            _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            log.LogWarning("Time zone {Zone} not found; using UTC.", options.Value.TimeZone);
            _zone = TimeZoneInfo.Utc;
        }
    }

    public DateTime Now => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime;
    public DateOnly Today => DateOnly.FromDateTime(Now);
}
