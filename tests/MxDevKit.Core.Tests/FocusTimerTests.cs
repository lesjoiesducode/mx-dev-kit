namespace MxDevKit.Tests;

public class FocusTimerTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    private static FocusTimer Timer(int focus = 25, int pause = 5) => new(TimeSpan.FromMinutes(focus), TimeSpan.FromMinutes(pause));

    [Fact]
    public void Idle_timer_shows_the_session_length()
    {
        var timer = Timer();

        Assert.Equal(FocusPhase.Idle, timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Remaining(T0));
        Assert.Equal(0, timer.Progress(T0));
    }

    [Fact]
    public void Session_runs_then_hands_over_to_the_break()
    {
        var timer = Timer();
        timer.Click(T0);

        Assert.Equal(FocusEvent.None, timer.Tick(T0.AddMinutes(10)));
        Assert.Equal(0.4, timer.Progress(T0.AddMinutes(10)), 3);
        Assert.Equal(FocusEvent.FocusEnded, timer.Tick(T0.AddMinutes(25)));
        Assert.Equal(FocusPhase.Break, timer.Phase);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Remaining(T0.AddMinutes(25)));
        Assert.Equal(FocusEvent.BreakEnded, timer.Tick(T0.AddMinutes(30)));
        Assert.Equal(FocusPhase.Idle, timer.Phase);
    }

    [Fact]
    public void Late_tick_still_starts_the_break_on_time()
    {
        var timer = Timer();
        timer.Click(T0);

        Assert.Equal(FocusEvent.FocusEnded, timer.Tick(T0.AddMinutes(27)));
        Assert.Equal(TimeSpan.FromMinutes(3), timer.Remaining(T0.AddMinutes(27)));
    }

    [Fact]
    public void Click_stops_a_session_and_skips_a_break()
    {
        var timer = Timer();
        timer.Click(T0);
        timer.Click(T0.AddMinutes(3));
        Assert.Equal(FocusPhase.Idle, timer.Phase);

        timer.Click(T0.AddMinutes(4));
        timer.Tick(T0.AddMinutes(29));
        Assert.Equal(FocusPhase.Break, timer.Phase);
        timer.Click(T0.AddMinutes(30));
        Assert.Equal(FocusPhase.Idle, timer.Phase);
    }

    [Fact]
    public void Without_break_the_timer_goes_back_to_idle()
    {
        var timer = Timer(pause: 0);
        timer.Click(T0);

        Assert.Equal(FocusEvent.FocusEnded, timer.Tick(T0.AddMinutes(25)));
        Assert.Equal(FocusPhase.Idle, timer.Phase);
    }

    [Theory]
    [InlineData(25 * 60, "25")]
    [InlineData(24 * 60 + 1, "25")]
    [InlineData(24 * 60, "24")]
    [InlineData(61, "2")]
    [InlineData(60, "1")]
    [InlineData(59, "59s")]
    [InlineData(0.2, "1s")]
    [InlineData(0, "0s")]
    public void Format_shows_minutes_then_seconds(double seconds, string expected) =>
        Assert.Equal(expected, FocusTimer.Format(TimeSpan.FromSeconds(seconds)));
}
