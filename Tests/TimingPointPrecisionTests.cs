namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using static GdUnit4.Assertions;

// Uses the real constructor (runtime required). The old xUnit test bypassed it with GetUninitializedObject + reflection.
[TestSuite]
[RequireGodotRuntime]
public class TimingPointPrecisionTests
{
    [TestCase]
    public void TimingPoint_preserves_double_precision_core_values()
    {
        TimingPoint point = AutoFree(new TimingPoint(time: 0d, timeSignature: [4, 4]))!;
        double offset = 2.473544123456789;
        double measurePosition = 310.00000000000006;
        double measuresPerSecond = TimingMath.BpmToMeasuresPerSecond(154.99335007388657, [4, 4]);

        point.Offset = offset;
        point.MeasurePosition = measurePosition;
        point.MeasuresPerSecond = measuresPerSecond;

        AssertThat(point.Offset).IsEqual(offset);
        AssertThat(point.MeasurePosition!.Value).IsEqual(measurePosition);
        AssertThat(point.MeasuresPerSecond).IsEqual(measuresPerSecond);
    }
}