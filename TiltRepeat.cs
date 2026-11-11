namespace SimpleViewer;

/// <summary>
/// Rate limit for the display-time controls (tilt wheel and +/- keys): the first event applies one step at once,
/// events that keep coming in the same direction apply one more step per <see cref="Interval"/>.
/// Tilt: a gap of <see cref="HoldGap"/> ends the hold. Keys: a non-repeat press is always a fresh first event.
/// The clock is injectable for tests.
/// </summary>
internal sealed class TiltRepeat(Func<TimeSpan> clock)
{
    public static readonly TimeSpan HoldGap = TimeSpan.FromMilliseconds(250);
    public TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(500);

    bool active;
    int lastDir;
    TimeSpan lastEvent, lastApplied;

    /// <summary>Tilt event with the given sign; returns the step to apply (-1, 0 or +1).</summary>
    public int Tilt(int sign) => Event(sign, fresh: !active || clock() - lastEvent >= HoldGap);

    /// <summary>Key press (+1/-1); auto-repeat events only step once per interval.</summary>
    public int Key(int sign, bool isRepeat) => Event(sign, fresh: !isRepeat || !active);

    /// <summary>Hold ended (key released).</summary>
    public void Reset() => active = false;

    int Event(int sign, bool fresh)
    {
        sign = Math.Sign(sign);
        if (sign == 0) return 0;
        var now = clock();
        lastEvent = now;
        // Fresh event or opposite direction: immediate; same direction while held: once per interval.
        bool repeat = !fresh && sign == lastDir;
        if (repeat && now - lastApplied < Interval) return 0;
        // Repeats stay on the interval grid (no drift from the event spacing); otherwise restart at now.
        lastApplied = repeat && now - lastApplied < 2 * Interval ? lastApplied + Interval : now;
        active = true;
        lastDir = sign;
        return sign;
    }
}
