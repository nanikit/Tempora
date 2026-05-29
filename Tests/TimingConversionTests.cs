namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TimingConversionTests
{
    [TestCase]
    public void Offset_and_measure_position_round_trip_long_segment_with_double_precision()
    {
        double startOffset = 2.4735441;
        double endOffset = 101.57457;
        double startMeasure = 2;
        double endMeasure = 66;
        double measuresPerSecond = TimingMath.CalculateMeasuresPerSecond(startMeasure, endMeasure, startOffset, endOffset);
        Timing timing = AutoFree(new Timing { IsInstantiating = true })!;
        timing.AddTimingPoint(startMeasure, startOffset, measuresPerSecond);
        timing.IsInstantiating = false;

        AssertThat(timing.MeasurePositionToOffset(endMeasure)).IsEqualApprox(endOffset, 1e-12);
        AssertThat(timing.OffsetToMeasurePosition(endOffset)).IsEqualApprox(endMeasure, 1e-12);
    }

    [TestCase(3, 4, 4, 1, 1d / 3d)]
    [TestCase(7, 8, 4, 1, 2d / 7d)]
    public void Relative_note_position_uses_double_precision_for_non_4_4_signatures(
        int upper,
        int lower,
        int divisor,
        int index,
        double expected)
    {
        double position = Timing.GetRelativeNotePosition([upper, lower], divisor, index);

        AssertThat(position).IsEqualApprox(expected, 1e-15);
    }
}