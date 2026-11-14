namespace Tempora.Tests;

using System;
using System.Globalization;
using System.IO;
using GdUnit4;
using Godot;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class SettingsPersistenceTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Invalid_display_values_use_defaults_without_blocking_other_saves(bool legacy)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.cfg");
        string legacyPath = Path.Combine(directory, "settings.txt");
        string source = legacy ? legacyPath : path;
        string invalid = legacy ? "SpectrogramStepSize;1\n" : "[settings]\nSpectrogramStepSize=1\n";
        try
        {
            File.WriteAllText(source, invalid);
            var store = new SettingsStore(path, legacyPath);
            Settings settings = store.Load();
            AssertThat(settings.SpectrogramStepSize).IsEqual(new Settings().SpectrogramStepSize);
            AssertThat(store.RejectedVisualSettings!.Value.StepSize).IsEqual(1);
            AssertThat(File.ReadAllText(source)).IsEqual(invalid);
            if (legacy)
                AssertThat(File.Exists(path)).IsFalse();

            var unsafeSettings = new Settings { SpectrogramStepSize = 1 };
            AssertThrown(() => store.Save(unsafeSettings)).IsInstanceOf<FormatException>();
            AssertThat(File.ReadAllText(source)).IsEqual(invalid);

            settings.MusicVolumeNormalized = 0.4;
            store.Save(settings, nameof(Settings.MusicVolumeNormalized));
            var restarted = new SettingsStore(path, legacyPath);
            AssertThat(restarted.Load().MusicVolumeNormalized).IsEqual(0.4d);
            AssertThat(restarted.Load().SpectrogramStepSize).IsEqual(new Settings().SpectrogramStepSize);
            if (!legacy)
                AssertThat(restarted.RejectedVisualSettings!.Value.StepSize).IsEqual(1);

            store.Save(settings);
            AssertThat(store.RejectedVisualSettings).IsNull();
            restarted.Load();
            AssertThat(restarted.RejectedVisualSettings).IsNull();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Invalid_numeric_display_values_are_retained_for_correction(bool legacy)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.cfg");
        string legacyPath = Path.Combine(directory, "settings.txt");
        string originalPath = legacy ? legacyPath : path;
        string invalid = legacy ? "SpectrogramIntensity;-2147483648\n" : "[settings]\nSpectrogramIntensity=-2147483648\n";
        try
        {
            File.WriteAllText(originalPath, invalid);
            var store = new SettingsStore(path, legacyPath);
            AssertThat(store.Load().SpectrogramIntensity).IsEqual(new Settings().SpectrogramIntensity);
            AssertThat(store.RejectedVisualSettings!.Value.Intensity).IsEqual(int.MinValue);
            AssertThat(File.ReadAllText(originalPath)).IsEqual(invalid);
            if (legacy)
                AssertThat(File.Exists(path)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase]
    public void Invalid_settings_are_reported_without_overwriting_the_file()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.cfg");
        const string invalid = "[settings]\nNumberOfRows=\"invalid\"\n";
        try
        {
            File.WriteAllText(path, invalid);
            AssertThrown(() => new SettingsStore(path, Path.Combine(directory, "settings.txt")).Load()).IsInstanceOf<FormatException>();
            AssertThat(File.ReadAllText(path)).IsEqual(invalid);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase]
    public void Existing_settings_survive_migration_and_restart()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.cfg");
        string legacyPath = Path.Combine(directory, "settings.txt");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            const string legacy = "HelpLanguage;ko\nNumberOfRows;17\nMusicVolumeNormalized;0,6\nSpectrogramIntensity;8\nSpectrogramMaxFreq;8000\nRoundBPM;False\n";
            File.WriteAllText(legacyPath, legacy);
            var store = new SettingsStore(path, legacyPath);
            Settings settings = store.Load();
            AssertThat(settings.TextScaleFactor).IsEqual(1d);
            AssertThat(settings.SpectrogramMinFreq).IsEqual(2200);
            AssertThat(settings.Language).IsEqual("ko");
            AssertThat(settings.MusicVolumeNormalized).IsEqual(0.6d);
            AssertThat(File.ReadAllText(legacyPath)).IsEqual(legacy);
            settings.Language = "ja";
            settings.TextScaleFactor = 1.2;
            settings.SpectrogramMinFreq = 3300;
            settings.ProjectFilesDirectory = "C:/music/曲;demo";
            store.Save(settings);
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Settings restarted = store.Load();
            AssertThat(restarted.TextScaleFactor).IsEqual(1.2d);
            AssertThat(restarted.SpectrogramMinFreq).IsEqual(3300);
            AssertThat(restarted.Language).IsEqual("ja");
            AssertThat(restarted.NumberOfRows).IsEqual(17);
            AssertThat(restarted.RoundBPM).IsFalse();
            AssertThat(restarted.SpectrogramIntensity).IsEqual(8);
            AssertThat(restarted.ProjectFilesDirectory).IsEqual("C:/music/曲;demo");
            AssertThat(restarted.MusicVolumeNormalized).IsEqual(0.6d);
            using var config = new ConfigFile();
            AssertThat(config.Load(path)).IsEqual(Error.Ok);
            AssertThat(config.GetValue("settings", "SpectrogramMinFreq").AsInt32()).IsEqual(3300);
            AssertThat(config.HasSectionKey("settings", "SpectrogramMaxFreq")).IsFalse();
            AssertThat(config.GetValue("settings", "HelpLanguage").AsString()).IsEqual("ja");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Directory.Delete(directory, true);
        }
    }
}