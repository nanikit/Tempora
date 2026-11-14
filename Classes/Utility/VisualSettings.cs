namespace Tempora.Classes.Utility;

public readonly record struct VisualSettings(double TextScaleFactor, int StepSize, int FftSize,
    int MinFreq, int Intensity, bool UseDb)
{
    public (int StepSize, int FftSize, int MinFreq, int Intensity, bool UseDb) Spectrogram
        => (StepSize, FftSize, MinFreq, Intensity, UseDb);

    public bool IsInRange => double.IsFinite(TextScaleFactor) && TextScaleFactor is >= 0.75 and <= 2
        && StepSize is >= 16 and <= 65536 && IsFftSizeInRange(FftSize)
        && MinFreq is >= 0 and <= 96000 && Intensity >= 0;

    public static VisualSettings From(Settings settings) => new(settings.TextScaleFactor,
        settings.SpectrogramStepSize, settings.SpectrogramFftSize, settings.SpectrogramMinFreq,
        settings.SpectrogramIntensity, settings.SpectrogramUseDb);

    public static bool IsFftSizeInRange(int size) => size is >= 32 and <= 16384 && (size & (size - 1)) == 0;

    public static bool TryParse(double scale, (string Step, int Fft, string Frequency, string Intensity) input,
        bool useDb, int sampleRate, int sampleCount, out VisualSettings values)
    {
        values = default;
        if (!int.TryParse(input.Step, out int step)
            || !int.TryParse(input.Frequency, out int frequency) || !int.TryParse(input.Intensity, out int intensity))
            return false;
        values = new VisualSettings(scale, step, input.Fft, frequency, intensity, useDb);
        return values.IsValid(sampleRate, sampleCount);
    }

    public bool IsValid(int sampleRate, int sampleCount)
    {
        if (!IsInRange || sampleRate < 1 || MinFreq >= sampleRate / 2 || sampleCount < 1)
            return false;
        var (columns, rows) = ImageSize(sampleRate, sampleCount);
        if (columns > Godot.Image.MaxWidth || rows > Godot.Image.MaxHeight || columns * rows * 4 > System.Array.MaxLength)
            return false;

        long work = EstimateFftWork(sampleRate, sampleCount);
        long baseline = From(new Settings()).EstimateFftWork(sampleRate, sampleCount);
        // Default settings must remain usable when a longer song raises both estimates.
        return work <= System.Math.Max(1L << 30, baseline);
    }

    public void ApplyTo(Settings settings)
    {
        settings.TextScaleFactor = TextScaleFactor;
        settings.SpectrogramStepSize = StepSize;
        settings.SpectrogramFftSize = FftSize;
        settings.SpectrogramMinFreq = MinFreq;
        settings.SpectrogramIntensity = Intensity;
        settings.SpectrogramUseDb = UseDb;
    }

    private (long Columns, long Rows) ImageSize(int sampleRate, int sampleCount)
        => (System.Math.Max(1, (sampleCount - FftSize) / StepSize), FftSize / 2 - (long)((double)MinFreq * FftSize / sampleRate));

    private long EstimateFftWork(int sampleRate, int sampleCount)
    {
        var (columns, _) = ImageSize(sampleRate, sampleCount);
        // Frequency cropping does not reduce the full FFT computed by the library.
        return columns * FftSize * System.Numerics.BitOperations.Log2((uint)FftSize);
    }
}
