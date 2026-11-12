namespace Tempora.Tests;

using System;
using GdUnit4;
using Godot;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TextSizingTests
{
    [TestCase]
    public void Text_settings_resize_existing_and_new_labels_without_accumulating()
    {
        using var sizing = new TextSizing();
        var window = new Window();
        try
        {
            var label = new Label();
            label.AddThemeFontSizeOverride("font_size", 14);
            window.AddChild(label);
            sizing.Register(window);
            sizing.Register(label);
            AssertThat(label.GetThemeFontSize("font_size")).IsEqual(18);
            sizing.ApplyScale(1.5, 1.2);
            AssertThat(label.GetThemeFontSize("font_size")).IsEqual(32);
            var newLabel = new Label();
            newLabel.AddThemeFontSizeOverride("font_size", 14);
            window.AddChild(newLabel);
            sizing.Register(newLabel);
            AssertThat(newLabel.GetThemeFontSize("font_size")).IsEqual(32);
            sizing.ApplyScale(1, 1);
            sizing.Register(label);
            AssertThat(label.GetThemeFontSize("font_size")).IsEqual(18);
            AssertThat(newLabel.GetThemeFontSize("font_size")).IsEqual(18);
            AssertThat(window.Theme.DefaultFontSize).IsEqual(20);
        }
        finally
        {
            window.Free();
        }
    }

    [TestCase]
    public void Windows_text_preference_can_be_read_and_unsubscribed()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var source = new WindowsTextScale();
        AssertThat(source.ScaleFactor).IsBetween(1d, 2.25d);
    }
}