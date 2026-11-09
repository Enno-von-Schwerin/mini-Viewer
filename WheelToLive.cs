namespace SimpleViewer;

/// <summary>Turns raw wheel deltas into full notches (120 units each); high-resolution wheels step once per notch.</summary>
internal sealed class WheelAccumulator
{
    int accum;
    public int Add(int delta)
    {
        accum += delta;
        int notches = accum / 120;
        accum -= notches * 120;
        return notches;
    }
    public void Clear() => accum = 0;
}

/// <summary>
/// Counts forward wheel notches at the last image while live mode is off. Step 1 is the usual "Letztes Bild",
/// steps 2..RequiredSteps-1 show a countdown, step RequiredSteps switches live mode on. The counter restarts when
/// more than Pause passes since the previous counted step or when the last image changed. Clock is injectable for tests.
/// </summary>
internal sealed class WheelToLive(Func<TimeSpan> clock)
{
    public const int RequiredSteps = 5; // including the "Letztes Bild" step

    TimeSpan last;
    string? lastImage;
    public int Count { get; private set; }
    public TimeSpan Pause { get; set; } = TimeSpan.FromSeconds(2);

    public void Reset() { Count = 0; lastImage = null; }

    /// <summary>Counts one forward notch at <paramref name="lastPath"/>; returns the step number (1..RequiredSteps).</summary>
    public int Step(string lastPath)
    {
        var now = clock();
        if (Count > 0 && (now - last > Pause || !string.Equals(lastPath, lastImage, StringComparison.OrdinalIgnoreCase))) Count = 0;
        int step = ++Count;
        last = now;
        lastImage = lastPath;
        if (step >= RequiredSteps) Reset(); // live mode goes on; start over next time
        return step;
    }

    /// <summary>
    /// A forward wheel notch was blocked at the last image. Returns the message to show, or null when live mode
    /// must be switched on now. With live mode already on (or the feature off) nothing is counted.
    /// </summary>
    public string? AtLastImage(string lastPath, bool live, bool enabled)
    {
        if (live || !enabled) { Reset(); return "Letztes Bild"; }
        return Message(Step(lastPath));
    }

    /// <summary>Message for a step; null for the final step (live mode shows its own confirmation).</summary>
    public static string? Message(int step) =>
        step <= 1 ? "Letztes Bild" : step < RequiredSteps ? $"Live Update Ein ({RequiredSteps - step})" : null;
}
