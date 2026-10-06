using System.Globalization;
using AICommentModerator.Application.Options;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Application.Bot;

/// <summary>Answers one question: is the office open right now?</summary>
public sealed class WorkingHoursCalendar
{
    private readonly IOptionsMonitor<BotOptions> _options;
    private readonly ILogger<WorkingHoursCalendar>? _logger;

    public WorkingHoursCalendar(IOptionsMonitor<BotOptions> options, ILogger<WorkingHoursCalendar>? logger = null)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Overridable clock so the tests can stand at any hour of any day.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public bool IsOpen() => IsOpenAt(Now());

    public bool IsOpenAt(DateTimeOffset instant)
    {
        var hours = _options.CurrentValue.WorkingHours;
        if (!hours.Enabled)
            return true;

        var local = TimeZoneInfo.ConvertTime(instant, ResolveZone(hours.TimeZone));

        if (IsHoliday(hours, local))
            return false;

        if (!IsWorkingDay(hours, local))
            return false;

        if (!TryParseTime(hours.From, out var from) || !TryParseTime(hours.To, out var to))
            return true;

        var time = local.TimeOfDay;

        // A shift that ends before it starts runs through midnight.
        return from <= to
            ? time >= from && time < to
            : time >= from || time < to;
    }

    private static bool IsHoliday(WorkingHoursOptions hours, DateTimeOffset local) =>
        hours.Holidays.Any(h =>
            DateOnly.TryParse(h, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) &&
            day == DateOnly.FromDateTime(local.Date));

    private static bool IsWorkingDay(WorkingHoursOptions hours, DateTimeOffset local)
    {
        if (hours.Days.Length == 0)
            return true;

        return hours.Days.Any(d =>
            Enum.TryParse<DayOfWeek>(d.Trim(), ignoreCase: true, out var day) && day == local.DayOfWeek);
    }

    private static bool TryParseTime(string value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value?.Trim(), new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out time);

    private TimeZoneInfo ResolveZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger?.LogWarning("Unknown time zone {TimeZone}; falling back to UTC", id);
            return TimeZoneInfo.Utc;
        }
    }
}
