using System;

namespace MxDevKit;

public enum FocusPhase
{
    Idle,
    Focus,
    Break,
}

public enum FocusEvent
{
    None,
    FocusEnded,
    BreakEnded,
}

// Click to start a focus session, click again to stop it. The break starts on its own and a click skips it.
public sealed class FocusTimer
{
    public FocusTimer(TimeSpan focus, TimeSpan pause)
    {
        this.FocusLength = focus;
        this.BreakLength = pause;
    }

    public TimeSpan FocusLength { get; set; }
    public TimeSpan BreakLength { get; set; }
    public FocusPhase Phase { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime EndsAt { get; private set; }

    public void Click(DateTime now)
    {
        if (this.Phase == FocusPhase.Idle)
        {
            this.Enter(FocusPhase.Focus, now, this.FocusLength);
        }
        else
        {
            this.Phase = FocusPhase.Idle;
        }
    }

    public FocusEvent Tick(DateTime now)
    {
        if (this.Phase == FocusPhase.Focus && now >= this.EndsAt)
        {
            if (this.BreakLength > TimeSpan.Zero)
            {
                this.Enter(FocusPhase.Break, this.EndsAt, this.BreakLength);
            }
            else
            {
                this.Phase = FocusPhase.Idle;
            }

            return FocusEvent.FocusEnded;
        }

        if (this.Phase == FocusPhase.Break && now >= this.EndsAt)
        {
            this.Phase = FocusPhase.Idle;
            return FocusEvent.BreakEnded;
        }

        return FocusEvent.None;
    }

    public TimeSpan Remaining(DateTime now) => this.Phase switch
    {
        FocusPhase.Idle => this.FocusLength,
        _ => this.EndsAt > now ? this.EndsAt - now : TimeSpan.Zero,
    };

    // 0 at the start of the phase, 1 at the end.
    public double Progress(DateTime now)
    {
        if (this.Phase == FocusPhase.Idle)
        {
            return 0;
        }

        var total = (this.EndsAt - this.StartedAt).TotalSeconds;
        return total <= 0 ? 1 : Math.Clamp((now - this.StartedAt).TotalSeconds / total, 0, 1);
    }

    // "25" while minutes are left, then "45s" for the last minute.
    public static string Format(TimeSpan remaining)
    {
        if (remaining.TotalSeconds >= 60)
        {
            return ((int)Math.Ceiling(remaining.TotalMinutes - 1e-9)).ToString();
        }

        return $"{(int)Math.Ceiling(remaining.TotalSeconds)}s";
    }

    private void Enter(FocusPhase phase, DateTime start, TimeSpan length)
    {
        this.Phase = phase;
        this.StartedAt = start;
        this.EndsAt = start + length;
    }
}
