namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
public class VisualSettingsTests
{
    [TestCase]
    public void Narrow_frequency_ranges_still_require_bounded_fft_work()
        => AssertThat(new VisualSettings(1, 16, 16384, 22049, 5, true).IsValid(44100, 44100 * 180)).IsFalse();

    [TestCase]
    public void Default_spectrum_remains_valid_for_a_longer_song()
    {
        var defaults = VisualSettings.From(new Settings());
        AssertThat(defaults.IsValid(48000, 48000 * 229)).IsTrue();
        AssertThat((defaults with { StepSize = 16, FftSize = 16384 }).IsValid(48000, 48000 * 229)).IsFalse();
    }

    [TestCase]
    public void Default_spectrum_supports_short_audio_but_respects_native_limits()
    {
        var defaults = VisualSettings.From(new Settings());
        AssertThat(defaults.IsValid(44100, 300)).IsTrue();
        AssertThat(defaults.IsValid(44100, 0)).IsFalse();
        AssertThat(defaults.IsValid(44100, int.MaxValue)).IsFalse();
    }

    [TestCase(15, 256, false)]
    [TestCase(16, 32, true)]
    [TestCase(65536, 16384, true)]
    [TestCase(65537, 256, false)]
    [TestCase(64, 16, false)]
    [TestCase(64, 32768, false)]
    public void Persisted_spectral_sizes_stay_inside_the_supported_range(int step, int fft, bool valid)
        => AssertThat(new VisualSettings(1, step, fft, 2200, 5, true).IsInRange).IsEqual(valid);

    [TestCase("", false)]
    [TestCase("0", false)]
    [TestCase("300", false)]
    [TestCase("2147483648", false)]
    [TestCase("512", true)]
    public void Incomplete_or_invalid_fft_input_keeps_the_last_preview(string fft, bool valid)
    {
        AssertThat(VisualSettings.TryParse(1.5, ("64", fft, "2200", "5"), true, 44100, 44100 * 30, out _))
            .IsEqual(valid);
    }

    [TestCase(1, 44100 * 180, false)]
    [TestCase(64, 44100 * 180, true)]
    public void Spectral_images_must_fit_the_native_image_and_array_limits(int step, int samples, bool valid)
        => AssertThat(new VisualSettings(1, step, 256, 2200, 5, true).IsValid(44100, samples)).IsEqual(valid);

    [TestCase(64, 44100 * 30, true)]
    [TestCase(1, 44100 * 180, false)]
    [TestCase(16, 44100 * 180, false)]
    public void Expensive_spectral_values_are_rejected_before_allocation(int step, int samples, bool preview)
    {
        var values = new VisualSettings(1, step, 256, 2200, 5, true);
        AssertThat(values.IsValid(44100, samples)).IsEqual(preview);
    }
}