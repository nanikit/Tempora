namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class BeatSaberExportTimingTests
{
    [TestCase]
    public void V4_bpm_regions_preserve_timing_point_tempo_until_sample_index_quantization()
    {
        TimingPoint start = MakePoint(offset: 2.4735441, measurePosition: 2, bpm: 154.99335007388657);
        TimingPoint end = MakePoint(offset: 101.57457, measurePosition: 66, bpm: 154.99335007388657);
        int sampleRate = 44100;

        var bpmDataPoints = BeatSaberExporter.GetBpmDataPoints([start, end], audioSamples: 5_000_000, sampleRate, audacityOrigin: 0);
        BeatSaberExporter.BpmDataPoint firstRegion = bpmDataPoints[0];

        double exportedBpm = (firstRegion.EndBeat - firstRegion.StartBeat)
            / (firstRegion.EndIndex - firstRegion.StartIndex)
            * sampleRate
            * 60d;

        AssertThat(firstRegion.StartBeat).IsEqual(0);
        AssertThat(firstRegion.EndBeat).IsEqual(256);
        AssertThat(exportedBpm).IsEqualApprox(154.99335866308343, 1e-12);
    }

    private static TimingPoint MakePoint(double offset, double measurePosition, double bpm)
    {
        TimingPoint point = AutoFree(new TimingPoint(offset, measurePosition, [4, 4]))!;
        point.MeasuresPerSecond = TimingMath.BpmToMeasuresPerSecond(bpm, point.TimeSignature);
        return point;
    }
}