namespace SimpleViewer;

internal enum AutoEndMode { Wait, Restart, Stop }

/// <summary>
/// Auto mode (slideshow) timing: display time T from a fixed list, countdown with pause/resume.
/// The clock is injectable for tests; the window polls <see cref="Due"/> and <see cref="Remaining"/>.
/// </summary>
internal sealed class AutoAdvance(Func<TimeSpan> clock)
{
    public static readonly int[] Times = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 30, 45, 60, 90, 120];
    public static bool IsValidTime(int s) => Array.IndexOf(Times, s) >= 0;

    TimeSpan start;      // clock time the countdown started, shifted forward by paused time
    TimeSpan? pausedAt;  // set while paused (elapsed time frozen)
    int seconds = 5;

    public bool On { get; private set; }
    public int Seconds { get => seconds; set => seconds = IsValidTime(value) ? value : 5; }
    TimeSpan T => TimeSpan.FromSeconds(seconds);
    public TimeSpan Elapsed => (pausedAt ?? clock()) - start;
    /// <summary>Bar fill 1 (just started) .. 0 (expired).</summary>
    public double Remaining => On ? Math.Clamp(1 - Elapsed / T, 0, 1) : 0;
    public bool Due => On && pausedAt == null && Elapsed >= T;

    /// <summary>Switches on/off; returns the new state. Cannot switch on without an image.</summary>
    public bool Toggle(bool hasImage)
    {
        On = !On && hasImage;
        if (On) Restart();
        return On;
    }

    public void Stop() => On = false;

    /// <summary>Starts the countdown over with a full bar (keeps the pause state).</summary>
    public void Restart()
    {
        start = clock();
        if (pausedAt != null) pausedAt = start;
    }

    public bool Paused
    {
        get => pausedAt != null;
        set
        {
            if (value && pausedAt == null) pausedAt = clock();
            else if (!value && pausedAt is TimeSpan p) { start += clock() - p; pausedAt = null; }
        }
    }

    /// <summary>Steps T through <see cref="Times"/> (clamped); +1 = slower, -1 = faster. Elapsed time is kept.</summary>
    public bool ChangeSpeed(int direction)
    {
        int i = Math.Clamp(Array.IndexOf(Times, seconds) + Math.Sign(direction), 0, Times.Length - 1);
        if (Times[i] == seconds) return false;
        seconds = Times[i];
        return true;
    }

    /// <summary>
    /// Decides what happens when the countdown expired. next: neighbor in sort order (equals current at the end).
    /// newer/first are read lazily, only at the end of the list. Returns the image to show (null = stay) and
    /// whether Auto mode ends.
    /// </summary>
    public static (string? Show, bool Stop) OnExpired(string current, string? next, Func<string?> newer, Func<string?> first, AutoEndMode mode)
    {
        static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (next != null && !Same(next, current)) return (next, false);
        if (newer() is string n && !Same(n, current)) return (n, false);
        return mode switch
        {
            AutoEndMode.Restart => first() is string f && !Same(f, current) ? (f, false) : (null, false),
            AutoEndMode.Stop => (null, true),
            _ => (null, false), // Wait: stay, countdown restarts, later images are picked up
        };
    }
}
