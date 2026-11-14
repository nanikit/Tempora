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
    public void Interface_and_system_text_sizes_apply_once_to_existing_and_new_controls()
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
            AssertThat(label.GetThemeFontSize("font_size")).IsEqual(26);
            AssertThat(window.ContentScaleFactor).IsEqualApprox(1.2f, 0.001f);
            int themeChanges = 0;
            window.Theme.Changed += () => themeChanges++;
            sizing.ApplyScale(1.5, 2);
            AssertThat(themeChanges).IsEqual(0);
            var newLabel = new Label();
            newLabel.AddThemeFontSizeOverride("font_size", 14);
            window.AddChild(newLabel);
            sizing.Register(newLabel);
            AssertThat(newLabel.GetThemeFontSize("font_size")).IsEqual(26);
            sizing.ApplyScale(1, 1);
            sizing.Register(label);
            AssertThat(label.GetThemeFontSize("font_size")).IsEqual(18);
            AssertThat(newLabel.GetThemeFontSize("font_size")).IsEqual(18);
            AssertThat(window.Theme.DefaultFontSize).IsEqual(20);
        }
        finally
        {
            sizing.Unregister(window);
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