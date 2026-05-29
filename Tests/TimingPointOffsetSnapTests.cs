namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

// The old xUnit test injected the timingPoints field via reflection.
// Here we build the same offset/measurePosition combination through the real AddTimingPoint.
[TestSuite]
[RequireGodotRuntime]
public class TimingPointOffsetSnapTests
{
    [TestCase]
    public void SnapOffsetChangeToBpmIncrement_stops_when_current_point_bpm_reaches_next_tenth()
    {
        Settings.Instance.RoundBPM = true;
        const double offsetChange = 0.002d;
        double initialOffsetDifference = 1d / TimingMath.BpmToMeasuresPerSecond(120.05d, [4, 4]);
        double targetOffsetDifference = 1d / TimingMath.BpmToMeasuresPerSecond(120.1d, [4, 4]);
        Timing timing = MakeTiming((offset: 1d, measurePosition: 1d), (1d + initialOffsetDifference, 2d));

        double snappedOffsetChange = timing.SnapOffsetChangeToBpmIncrement(0, 0, offsetChange);

        AssertThat(snappedOffsetChange).IsEqualApprox(initialOffsetDifference - targetOffsetDifference, 1e-12);
    }

    [TestCase]
    public void SnapOffsetChangeToBpmIncrement_stops_when_previous_point_bpm_reaches_next_tenth()
    {
        Settings.Instance.RoundBPM = true;
        const double offsetChange = 0.002d;
        double initialOffsetDifference = 1d / TimingMath.BpmToMeasuresPerSecond(120.05d, [4, 4]);
        double targetOffsetDifference = 1d / TimingMath.BpmToMeasuresPerSecond(120d, [4, 4]);
        Timing timing = MakeTiming((offset: 0d, measurePosition: 0d), (initialOffsetDifference, 1d));

        double snappedOffsetChange = timing.SnapOffsetChangeToBpmIncrement(1, 1, offsetChange);

        AssertThat(snappedOffsetChange).IsEqualApprox(targetOffsetDifference - initialOffsetDifference, 1e-12);
    }

    [TestCase]
    public void SnapOffsetChangeToBpmIncrement_keeps_requested_change_when_tenth_is_farther_than_step()
    {
        Settings.Instance.RoundBPM = true;
        const double offsetChange = 0.0001d;
        double initialOffsetDifference = 1d / TimingMath.BpmToMeasuresPerSecond(120.05d, [4, 4]);
        Timing timing = MakeTiming((offset: 1d, measurePosition: 1d), (1d + initialOffsetDifference, 2d));

        double snappedOffsetChange = timing.SnapOffsetChangeToBpmIncrement(0, 0, offsetChange);

        AssertThat(snappedOffsetChange).IsEqualApprox(offsetChange, 1e-12);
    }

    [TestCase]
    public void SnapOffsetChangeToBpmIncrement_keeps_requested_change_when_round_bpm_is_disabled()
    {
        Settings.Instance.RoundBPM = false;
        const double offsetChange = 0.002d;
        double initialOffsetDifference = 1d / TimingMath.BpmToMeasuresPerSecond(120.05d, [4, 4]);
        Timing timing = MakeTiming((offset: 1d, measurePosition: 1d), (1d + initialOffsetDifference, 2d));

        double snappedOffsetChange = timing.SnapOffsetChangeToBpmIncrement(0, 0, offsetChange);

        AssertThat(snappedOffsetChange).IsEqualApprox(offsetChange, 1e-12);
    }

    private static Timing MakeTiming(params (double offset, double measurePosition)[] points)
    {
        // AddTimingPoint's MPS recalculation raises the global TimingChanged event, and ProjectFileManager
        // decides whether to AutoSave based on Timing.Instance.IsInstantiating. So assign the test timing as
        // Instance and keep IsInstantiating on during construction.
        Timing timing = AutoFree(new Timing { IsInstantiating = true })!;
        Timing.Instance = timing;
        foreach ((double offset, double measurePosition) in points)
            timing.AddTimingPoint(measurePosition, offset);
        timing.IsInstantiating = false;
        return timing;
    }
}