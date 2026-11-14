using System;
using Godot;
using Tempora.Classes.Audio;

namespace Tempora.Classes.Visual.AudioDisplay;
public partial class SpectrogramSegment : Sprite2D, IAudioSegmentDisplay
{
    public float[] TimeRange { get; set; } = null!;

    public float Width { get; set; } = 400;

    public float Height { get; set; } = 100;

    public AudioFile AudioFile { get; set; } = null!;

    public Color Color { get; set; } = DefaultColor;

    public static readonly Color DefaultColor = GlobalConstants.AudioFullExposure;
    private SpectrogramContext spectrogramContext = null!;

    public override void _Ready()
    {
        Render();
    }

    public SpectrogramSegment() { }

    public SpectrogramSegment(AudioFile audioFile, SpectrogramContext spectrogramContext, float length, float height, float[] timeRange)
    {
        InstantiateAndRender(audioFile, spectrogramContext, length, height, timeRange);
    }

    /// <summary>
    /// Useful when the object is already in the scene tree and needs to be re-instantiated with new data.
    /// </summary>
    public void InstantiateAndRender(AudioFile audioFile, SpectrogramContext spectrogramContext, float length, float height, float[] timeRange)
    {
        AudioFile = audioFile;
        this.spectrogramContext = spectrogramContext;
        Height = height;
        Width = length;
        TimeRange = timeRange;
        Render();
    }

    public void Render()
    {
        if (Width <= 0 || Height <= 0)
        {
            Texture = null;
            return;
        }
        int sampleIndexStart = AudioFile.SampleTimeToSampleIndex(TimeRange[0]);
        int sampleEndIndex = AudioFile.SampleTimeToSampleIndex(TimeRange[1]);

        Texture = spectrogramContext.GetSpectrogramSlice(sampleIndexStart, sampleEndIndex, (int)Math.Ceiling(Height), (int)Math.Ceiling(Width));
        Scale = new Vector2(Width, Height) / Texture.GetSize();
    }
}
