namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

// The old xUnit test injected the manualBpm/measuresPerSecond private fields via reflection.
// Here we reach the same state through the public API (SetManualBpm, MeasuresPerSecond, Settings.RoundBPM).
[TestSuite]
[RequireGodotRuntime]
public class TimingPointBpmTests
{
    [TestCase]
    public void ComputedBpm_uses_measures_per_second_without_manual_cache_rounding()
    {
        TimingPoint point = MakePoint(154.99335007388657 / 240d);

        AssertThat(point.ComputedBpm).IsEqualApprox(154.99335007388657, 1e-4);
        AssertThat(point.ManualBpm.HasValue).IsFalse();
    }

    [TestCase]
    public void Bpm_returns_computed_bpm_when_no_manual_bpm_exists()
    {
        TimingPoint point = MakePoint(154.99335007388657 / 240d);

        AssertThat(point.Bpm).IsEqual(point.ComputedBpm);
    }

    [TestCase]
    public void Bpm_returns_manual_bpm_without_changing_computed_bpm()
    {
        Settings.Instance.RoundBPM = false;
        TimingPoint point = MakePoint(154.99335007388657 / 240d);

        point.SetManualBpm(155d);

        AssertThat(point.Bpm).IsEqual(155d);
        AssertThat(point.ComputedBpm).IsEqualApprox(154.99335007388657, 1e-4);
        AssertThat(point.ManualBpm!.Value).IsEqual(155d);
    }

    [TestCase]
    public void Rounded_manual_bpm_does_not_change_computed_bpm()
    {
        Settings.Instance.RoundBPM = true;
        double measuresPerSecond = 154.99335007388657 / 240d;
        TimingPoint point = MakePoint(measuresPerSecond);

        point.SetManualBpm(154.99335007388657);

        AssertThat(point.Bpm).IsEqual(155d);
        AssertThat(point.MeasuresPerSecond).IsEqual(measuresPerSecond);
        AssertThat(point.ComputedBpm).IsEqualApprox(154.99335007388657, 1e-12);
    }

    [TestCase]
    public void ClearManualBpm_returns_bpm_to_computed_value()
    {
        Settings.Instance.RoundBPM = false;
        TimingPoint point = MakePoint(154.99335007388657 / 240d);
        point.SetManualBpm(155d);

        point.ClearManualBpm();

        AssertThat(point.ManualBpm.HasValue).IsFalse();
        AssertThat(point.Bpm).IsEqual(point.ComputedBpm);
    }

    [TestCase]
    public void SetManualBpm_does_not_mark_point_manual_when_change_is_rejected()
    {
        Settings.Instance.RoundBPM = false;
        TimingPoint point = MakePoint(0.5d);
        point.PropertyChanged += (_, e) =>
        {
            if (e is TimingPoint.PropertyChangeArgument change
                && change.PropertyType == TimingPoint.PropertyType.Bpm
                && (double)change.NewValue! == 130d)
                point.ClearManualBpm();
        };

        point.SetManualBpm(130d);

        AssertThat(point.HasManualBpm).IsFalse();
        AssertThat(point.ManualBpm.HasValue).IsFalse();
        AssertThat(point.Bpm).IsEqual(point.ComputedBpm);
    }

    [TestCase]
    public void SetManualBpm_preserves_existing_manual_bpm_when_new_change_is_rejected()
    {
        Settings.Instance.RoundBPM = false;
        TimingPoint point = MakePoint(0.5d);
        point.SetManualBpm(125d);
        point.PropertyChanged += (_, e) =>
        {
            if (e is TimingPoint.PropertyChangeArgument change
                && change.PropertyType == TimingPoint.PropertyType.Bpm
                && (double)change.NewValue! == 130d)
                point.ClearManualBpm();
        };

        point.SetManualBpm(130d);

        AssertThat(point.HasManualBpm).IsTrue();
        AssertThat(point.ManualBpm!.Value).IsEqual(125d);
        AssertThat(point.Bpm).IsEqual(125d);
    }

    [TestCase]
    public void MpsToBpm_can_use_explicit_time_signature_for_preserving_previous_bpm()
    {
        float mps = TimingPoint.BpmToMps(155f, [4, 4]);

        AssertThat(TimingPoint.MpsToBpm(mps, [4, 4])).IsEqual(155f);
    }

    private static TimingPoint MakePoint(double measuresPerSecond)
    {
        TimingPoint point = AutoFree(new TimingPoint(time: 0d, timeSignature: [4, 4]))!;
        point.MeasuresPerSecond = measuresPerSecond;
        return point;
    }
}