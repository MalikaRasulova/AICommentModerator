using AICommentModerator.Application.Bot;
using AICommentModerator.Application.Options;
using Xunit;

namespace AICommentModerator.Tests;

public class WorkingHoursCalendarTests
{
    // Tashkent is UTC+5 and has no daylight saving.
    private static readonly DateTimeOffset ThursdayTenAm = new(2026, 10, 8, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ThursdayEightPm = new(2026, 10, 8, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SaturdayNoon = new(2026, 10, 10, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_disabled_schedule_is_always_open()
    {
        var calendar = Calendar(new WorkingHoursOptions { Enabled = false });
        Assert.True(calendar.IsOpenAt(SaturdayNoon));
    }

    [Fact]
    public void Inside_the_working_day_it_is_open()
    {
        Assert.True(Calendar(Weekdays()).IsOpenAt(ThursdayTenAm));
    }

    [Fact]
    public void After_closing_time_it_is_shut()
    {
        Assert.False(Calendar(Weekdays()).IsOpenAt(ThursdayEightPm));
    }

    [Fact]
    public void A_day_off_is_shut_even_at_noon()
    {
        Assert.False(Calendar(Weekdays()).IsOpenAt(SaturdayNoon));
    }

    [Fact]
    public void A_night_shift_crossing_midnight_is_handled()
    {
        var nightShift = Weekdays();
        nightShift.From = "22:00";
        nightShift.To = "06:00";
        nightShift.Days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };

        var calendar = Calendar(nightShift);

        // Thursday 23:00 Tashkent = 18:00 UTC
        Assert.True(calendar.IsOpenAt(new DateTimeOffset(2026, 10, 8, 18, 0, 0, TimeSpan.Zero)));
        // Thursday 02:00 Tashkent = Wednesday 21:00 UTC
        Assert.True(calendar.IsOpenAt(new DateTimeOffset(2026, 10, 7, 21, 0, 0, TimeSpan.Zero)));
        Assert.False(calendar.IsOpenAt(ThursdayTenAm));
    }

    [Fact]
    public void A_holiday_closes_the_day()
    {
        var hours = Weekdays();
        hours.Holidays = new[] { "2026-10-08" };

        Assert.False(Calendar(hours).IsOpenAt(ThursdayTenAm));
    }

    [Fact]
    public void An_empty_day_list_means_every_day()
    {
        var hours = Weekdays();
        hours.Days = Array.Empty<string>();

        Assert.True(Calendar(hours).IsOpenAt(SaturdayNoon));
    }

    [Fact]
    public void An_unknown_time_zone_falls_back_to_utc_instead_of_crashing()
    {
        var hours = Weekdays();
        hours.TimeZone = "Mars/Olympus";

        // 05:00 UTC is before the 09:00 opening, so UTC says closed.
        Assert.False(Calendar(hours).IsOpenAt(ThursdayTenAm));
    }

    [Fact]
    public void An_unreadable_time_is_treated_as_always_open_rather_than_always_shut()
    {
        var hours = Weekdays();
        hours.From = "nine o'clock";

        Assert.True(Calendar(hours).IsOpenAt(ThursdayEightPm));
    }

    private static WorkingHoursOptions Weekdays() => new()
    {
        Enabled = true,
        TimeZone = "Asia/Tashkent",
        Days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
        From = "09:00",
        To = "18:00"
    };

    private static WorkingHoursCalendar Calendar(WorkingHoursOptions hours) =>
        new(new TestOptionsMonitor<BotOptions>(new BotOptions { WorkingHours = hours }));
}
