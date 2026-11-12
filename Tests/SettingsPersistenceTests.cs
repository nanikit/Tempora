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
    public void Invalid_numeric_settings_reach_the_load_error_handler(bool legacy)
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
            AssertThrown(() => new SettingsStore(path, legacyPath).Load()).IsInstanceOf<OverflowException>();
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