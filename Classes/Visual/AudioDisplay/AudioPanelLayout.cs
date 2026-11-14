namespace Tempora.Classes.Visual.AudioDisplay;

public readonly record struct AudioPanelLayout(int NominalMeasure, float Overlap, float DownbeatOffset)
{
    public float StartMeasure => NominalMeasure - Overlap - DownbeatOffset;
    public float EndMeasure => StartMeasure + MeasureLength;
    private float MeasureLength => 1 + 2 * Overlap;

    public float MeasurePositionToXPosition(float measurePosition, float width)
        => width * (measurePosition - StartMeasure) / MeasureLength;

    public float XPositionToMeasurePosition(float x, float width)
        => x * MeasureLength / width + StartMeasure;
}
