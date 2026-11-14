using System;
using Godot;
using Spectrogram;
using Tempora.Classes.Audio;
using Tempora.Classes.DataHelpers;
using Tempora.Classes.Utility;

namespace Tempora.Classes.Visual.AudioDisplay;

public sealed class SpectrogramContext : IDisposable
{
    private readonly PcmData pcmData;
    private readonly (int StepSize, int FftSize, int MinFreq) calculation;
    private (int Intensity, bool UseDb) imageSettings;

    public Godot.Image? CachedSpectrogramImage { get; private set; }
    public SpectrogramGenerator SpectrogramGenerator { get; }
    public int Intensity { get; set; }
    public bool DB { get; set; }

    public SpectrogramContext(PcmData pcmData, VisualSettings settings)
    {
        this.pcmData = pcmData;
        calculation = (settings.StepSize, settings.FftSize, settings.MinFreq);
        SpectrogramGenerator = SpectrogramHelper.GetSpectrogramGenerator(
            pcmData, settings.StepSize, settings.FftSize, settings.MinFreq);
        Intensity = settings.Intensity;
        DB = settings.UseDb;
    }

    public bool CanReuse(PcmData audio, VisualSettings settings)
        => ReferenceEquals(pcmData, audio)
            && calculation == (settings.StepSize, settings.FftSize, settings.MinFreq);

    public bool UpdateSpectrogram()
    {
        if (CachedSpectrogramImage != null && imageSettings == (Intensity, DB))
            return false;

        Godot.Image image = SpectrogramHelper.GenerateGodotImage(
            SpectrogramGenerator, SpectrogramHelper.TemporaColormap, Intensity, DB);
        CachedSpectrogramImage?.Dispose();
        CachedSpectrogramImage = image;
        imageSettings = (Intensity, DB);
        return true;
    }

    public ImageTexture GetSpectrogramSlice(int sampleStart, int sampleEnd, int targetHeight, int targetWidth)
    {
        UpdateSpectrogram();
        return SpectrogramHelper.GetSpectrogramSlice(
            CachedSpectrogramImage!, SampleToX(sampleStart), SampleToX(sampleEnd), targetHeight, targetWidth);
    }

    public void Dispose()
    {
        CachedSpectrogramImage?.Dispose();
        CachedSpectrogramImage = null;
    }

    private int SampleToX(int sample)
        => (int)((float)sample / pcmData.PcmFloats[0].Length * CachedSpectrogramImage!.GetWidth());
}