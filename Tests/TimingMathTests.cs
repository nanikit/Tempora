namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using static GdUnit4.Assertions;

// Pure static math, so no Godot runtime is needed ([RequireGodotRuntime] omitted to avoid the boot cost).
[TestSuite]
public class TimingMathTests
{
    [TestCase(155.0)]
    [TestCase(154.99335007388657)]
    [TestCase(120.00001)]
    public void Measures_per_second_and_bpm_round_trip_with_double_precision(double bpm)
    {
        double measuresPerSecond = TimingMath.BpmToMeasuresPerSecond(bpm, [4, 4]);

        AssertThat(TimingMath.MeasuresPerSecondToBpm(measuresPerSecond, [4, 4])).IsEqualApprox(bpm, 1e-12);
    }

    [TestCase]
    public void Adjacent_timing_points_calculate_measures_per_second_with_double_precision()
    {
        double measuresPerSecond = TimingMath.CalculateMeasuresPerSecond(
            startMeasurePosition: 2,
            endMeasurePosition: 66,
            startOffset: 2.4735441,
            endOffset: 101.57457);

        double bpm = TimingMath.MeasuresPerSecondToBpm(measuresPerSecond, [4, 4]);

        AssertThat(bpm).IsEqualApprox(154.99335007388657, 1e-12);
    }
}
