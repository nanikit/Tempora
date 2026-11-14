namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

[TestSuite]
public class AudioPanelLayoutTests
{
    private static readonly TimingAnchor[] Timing = [new(-1, -0.5, 0.5), new(0.5, 0.25, 0.5), new(200, 100, 0.5)];

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(100)]
    public void Rows_outside_the_song_have_no_audio(int measure)
    {
        var layout = new AudioPanelLayout(measure, 0, 0);
        AssertThat(TimingMath.GetPanelSegments(layout.StartMeasure, layout.EndMeasure, 3, Timing)).IsEmpty();
    }

    [TestCase]
    public void Audio_is_split_at_tempo_changes_and_song_edges()
    {
        var segments = TimingMath.GetPanelSegments(-0.25, 2, 3, Timing);
        AssertThat(segments.Count).IsEqual(2);
        AssertThat(segments[0]).IsEqual(new[] { 0f, 0.5f });
        AssertThat(segments[1]).IsEqual(new[] { 0.5f, 3f });
    }

    [TestCase]
    public void Boundary_points_do_not_create_empty_segments()
    {
        TimingAnchor[] points = [new(0, 0, 0.5), new(1, 0.5, 1), new(1.5, 1, 1)];
        var segments = TimingMath.GetPanelSegments(0, 1, 10, points);
        AssertThat(segments.Count).IsEqual(2);
        AssertThat(segments[0]).IsEqual(new[] { 0f, 1f });
        AssertThat(segments[1]).IsEqual(new[] { 1f, 1.5f });
    }

    [TestCase]
    public void Untimed_audio_uses_the_default_tempo()
    {
        var segments = TimingMath.GetPanelSegments(1, 2, 3, []);
        AssertThat(segments.Count).IsEqual(1);
        AssertThat(segments[0]).IsEqual(new[] { 2f, 3f });
    }

    [TestCase]
    public void Audio_before_the_first_point_uses_its_tempo()
    {
        var segments = TimingMath.GetPanelSegments(0, 1, 10, [new(2, 1, 1)]);
        AssertThat(segments.Count).IsEqual(1);
        AssertThat(segments[0]).IsEqual(new[] { 1f, 2f });
    }

    [TestCase]
    public void Nearby_measure_boundaries_keep_the_timing_tolerance()
    {
        TimingAnchor[] points = [new(0, 0, 0.5), new(1, 0.5, 1)];
        AssertThat(TimingMath.MeasurePositionToOffset(0.4995, points)).IsEqualApprox(0.9995, 1e-12);
    }

    [TestCase(-0.5)]
    [TestCase(0.25)]
    [TestCase(0.75)]
    [TestCase(2.0)]
    public void Time_and_measure_round_trip_across_tempo_changes(double measure)
    {
        TimingAnchor[] points = [new(0, 0, 0.5), new(1, 0.5, 1), new(1.5, 1, 0.25)];
        double offset = TimingMath.MeasurePositionToOffset(measure, points);
        AssertThat(TimingMath.OffsetToMeasurePosition(offset, points)).IsEqualApprox(measure, 1e-12);
    }

    [TestCase(0f)]
    [TestCase(0.25f)]
    public void Overlapping_rows_round_trip(float overlap)
    {
        var first = new AudioPanelLayout(3, overlap, 0.125f);
        var next = new AudioPanelLayout(4, overlap, 0.125f);
        AssertThat(first.MeasurePositionToXPosition(first.StartMeasure, 600)).IsEqualApprox(0f, 0.001f);
        AssertThat(first.MeasurePositionToXPosition(first.EndMeasure, 600)).IsEqualApprox(600f, 0.001f);
        foreach (float measure in new[] { 2f, 3f, 3.5f, 4f, 5f })
        {
            float x = first.MeasurePositionToXPosition(measure, 600);
            AssertThat(first.XPositionToMeasurePosition(x, 600)).IsEqualApprox(measure, 0.00001f);
            float nextX = next.MeasurePositionToXPosition(measure, 900);
            AssertThat(next.XPositionToMeasurePosition(nextX, 900)).IsEqualApprox(measure, 0.00001f);
        }
    }
}
