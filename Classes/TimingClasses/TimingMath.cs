namespace Tempora.Classes.TimingClasses;

public static class TimingMath
{
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
}