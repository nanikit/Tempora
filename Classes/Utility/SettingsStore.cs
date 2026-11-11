using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;

namespace Tempora.Classes.Utility;

public sealed class SettingsStore(string path = "user://settings.cfg", string legacyPath = "user://settings.txt")
{
    private const string Section = "settings";
    private static readonly (PropertyInfo Property, string Key)[] Properties = typeof(Settings)
        .GetProperties().Where(property => property.CanWrite)
        .Select(property => (property, property.GetCustomAttribute<SettingKeyAttribute>()?.Key ?? property.Name)).ToArray();

    public Settings Load()
    {
        var settings = new Settings();
        if (!Godot.FileAccess.FileExists(path))
        {
            if (Godot.FileAccess.FileExists(legacyPath))
            {
                LoadLegacy(settings);
                Save(settings);
            }
            return settings;
        }

        using var config = new ConfigFile();
        Error error = config.Load(path);
        if (error != Error.Ok)
            throw new IOException($"Could not load {path}: {error}. The file has been preserved.");
        foreach (var (property, key) in Properties)
        {
            if (config.HasSectionKey(Section, key))
                property.SetValue(settings, ReadValue(config.GetValue(Section, key), property.PropertyType, key),
                    BindingFlags.DoNotWrapExceptions, binder: null, index: null, culture: null);
        }
        return settings;
    }

    public void Save(Settings settings)
    {
        using var config = new ConfigFile();
        foreach (var (property, key) in Properties)
            config.SetValue(Section, key, ToVariant(property.GetValue(settings)!));
        Error error = config.Save(path);
        if (error != Error.Ok)
            throw new IOException($"Could not save {path}: {error}.");
    }

    private void LoadLegacy(Settings settings)
    {
        using var file = Godot.FileAccess.Open(legacyPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
            throw new IOException($"Could not load {legacyPath}: {Godot.FileAccess.GetOpenError()}.");
        foreach (string line in file.GetAsText().Split('\n'))
        {
            string[] pair = line.TrimEnd('\r').Split(';', 2);
            if (pair.Length != 2 || pair[1].Length == 0)
                continue;
            var entry = Properties.FirstOrDefault(entry => entry.Key == pair[0]);
            if (entry.Property == null)
                continue;
            entry.Property.SetValue(settings, ParseLegacyValue(pair[1], entry.Property.PropertyType),
                BindingFlags.DoNotWrapExceptions, binder: null, index: null, culture: null);
        }
    }

    private static object ParseLegacyValue(string text, Type type)
    {
        if (type == typeof(string))
            return text;
        if (type == typeof(bool))
            return bool.Parse(text);
        if (type == typeof(int))
            return int.Parse(text, CultureInfo.InvariantCulture);
        // Old files used the current culture; disallow thousands separators so 0,6 cannot become 6.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            number = double.Parse(text, NumberStyles.Float, CultureInfo.CurrentCulture);
        if (type == typeof(float))
            return (float)number;
        return number;
    }

    private static object ReadValue(Variant value, Type type, string key)
    {
        if (type == typeof(string) && value.VariantType == Variant.Type.String)
            return value.AsString();
        if (type == typeof(bool) && value.VariantType == Variant.Type.Bool)
            return value.AsBool();
        if (type == typeof(int) && value.VariantType == Variant.Type.Int)
            return checked((int)value.AsInt64());
        if (value.VariantType is Variant.Type.Int or Variant.Type.Float)
        {
            if (type == typeof(float))
                return value.AsSingle();
            if (type == typeof(double))
                return value.AsDouble();
        }
        throw new FormatException($"Invalid value for setting {key}: expected {type.Name}.");
    }

    private static Variant ToVariant(object value) => value switch
    {
        string text => text,
        bool flag => flag,
        int number => number,
        float number => number,
        double number => number,
        _ => throw new NotSupportedException($"Unsupported setting type: {value.GetType().Name}")
    };
}