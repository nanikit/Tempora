namespace Tempora.Classes.TimingClasses;

using System;
using System.Collections.Generic;

public static class TimingMath
{
    public static List<float[]> GetPanelSegments(double startMeasure, double endMeasure, float audioLength, IReadOnlyList<TimingAnchor> timing)
    {
        List<float[]> segments = new(2);
        float start = MathF.Max((float)MeasurePositionToOffset(startMeasure, timing), 0);
        float end = MathF.Min((float)MeasurePositionToOffset(endMeasure, timing), audioLength);
        if (start >= end)
            return segments;

        int firstPoint = GetOperatingAnchorIndex(startMeasure, timing) + 1;
        for (int index = firstPoint; index < timing.Count && timing[index].MeasurePosition < endMeasure; index++)
        {
            float boundary = Math.Clamp((float)timing[index].Offset, start, end);
            if (start < boundary)
                segments.Add([start, boundary]);
            start = boundary;
        }
        if (start < end)
            segments.Add([start, end]);
        return segments;
    }

    public static double MeasurePositionToOffset(double measurePosition, IReadOnlyList<TimingAnchor> timing)
    {
        int index = GetOperatingAnchorIndex(measurePosition, timing);
        if (index < 0)
            return measurePosition / 0.5d;
        TimingAnchor anchor = timing[index];
        if (anchor.MeasuresPerSecond <= 0)
            throw new ArgumentException("Operating timing point has MeasuresPerSecond <= 0", nameof(timing));
        return anchor.Offset + (measurePosition - anchor.MeasurePosition) / anchor.MeasuresPerSecond;
    }

    public static double OffsetToMeasurePosition(double offset, IReadOnlyList<TimingAnchor> timing)
    {
        if (timing.Count == 0)
            return offset * 0.5d;
        int index = timing.Count - 1;
        while (index > 0 && timing[index].Offset > offset)
            index--;
        TimingAnchor anchor = timing[index];
        return (offset - anchor.Offset) * anchor.MeasuresPerSecond + anchor.MeasurePosition;
    }

    public static double BpmToMeasuresPerSecond(double bpm, int[] timeSignature)
        => bpm / (60d * BeatsPerMeasure(timeSignature));

    public static double MeasuresPerSecondToBpm(double measuresPerSecond, int[] timeSignature)
        => measuresPerSecond * 60d * BeatsPerMeasure(timeSignature);

    public static double CalculateMeasuresPerSecond(
        double startMeasurePosition,
        double endMeasurePosition,
        double startOffset,
        double endOffset)
        => (endMeasurePosition - startMeasurePosition) / (endOffset - startOffset);

    private static double BeatsPerMeasure(int[] timeSignature)
        => timeSignature[0] * 4d / timeSignature[1];

    private static int GetOperatingAnchorIndex(double measurePosition, IReadOnlyList<TimingAnchor> timing)
    {
        // Keep the timing editor's measure tolerance and extrapolate the first anchor backwards.
        int index = timing.Count - 1;
        while (index > 0 && timing[index].MeasurePosition - measurePosition >= 0.001d)
            index--;
        return index;
    }
}
