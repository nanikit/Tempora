using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Tempora.Classes.Visual;

public sealed class TextSizing : IDisposable
{
    public const double BaseScale = 1.25;
    private readonly Theme theme = new();
    private readonly Dictionary<Node, (StringName Name, int Size)[]> overrides = new();
    private readonly InterfaceSizing interfaceSizing = new();
    private double scale = BaseScale;

    public TextSizing()
    {
        theme.DefaultFontSize = ScaleSize(16);
        UpdateWindowTitle();
        WidenSliderHandles();
    }

    public void Register(Node node)
    {
        if (node is not Control && node is not Window)
            return;
        if (!overrides.ContainsKey(node))
        {
            overrides[node] = node.GetPropertyList()
                .Select(property => property["name"].AsString())
                .Where(name => name.StartsWith("theme_override_font_sizes/", StringComparison.Ordinal))
                .Select(name => ((StringName)name, node.Get(name).AsInt32())).ToArray();
        }
        if (node is Window window)
        {
            window.Theme = theme;
            interfaceSizing.Register(window);
        }
        ApplyOverrides(node, overrides[node]);
    }

    public void Unregister(Node node)
    {
        overrides.Remove(node);
        if (node is Window window)
            interfaceSizing.Unregister(window);
    }

    public void ApplyScale(double systemScale, double userScale)
    {
        if (scale != BaseScale * systemScale)
        {
            scale = BaseScale * systemScale;
            theme.DefaultFontSize = ScaleSize(16);
            UpdateWindowTitle();
            foreach (var (node, sizes) in overrides.ToArray())
                ApplyOverrides(node, sizes);
        }
        interfaceSizing.ApplyScale((float)userScale);
    }

    public void Dispose()
    {
        interfaceSizing.Dispose();
        theme.Dispose();
    }

    private void WidenSliderHandles()
    {
        const int width = 24;
        Theme defaults = ThemeDB.GetDefaultTheme();
        foreach (string type in new[] { "HSlider", "VSlider" })
        {
            foreach (string name in new[] { "grabber", "grabber_highlight", "grabber_disabled" })
            {
                using Image image = defaults.GetIcon(name, type).GetImage();
                using ImageTexture texture = ImageTexture.CreateFromImage(image);
                texture.SetSizeOverride(new Vector2I(width, image.GetHeight()));
                theme.SetIcon(name, type, texture);
            }
        }
        foreach (string type in new[] { "HScrollBar", "VScrollBar" })
        {
            foreach (string name in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
            {
                using var style = (StyleBox)defaults.GetStylebox(name, type).Duplicate();
                style.ContentMarginLeft = width / 2;
                style.ContentMarginRight = width / 2;
                theme.SetStylebox(name, type, style);
            }
        }
    }

    private void UpdateWindowTitle()
    {
        int height = Math.Max(36, (int)Math.Ceiling(ThemeDB.FallbackFont.GetHeight(theme.DefaultFontSize)) + 12);
        theme.SetConstant("title_height", "Window", height);
        theme.SetConstant("close_v_offset", "Window", (height + 12) / 2);
        foreach (string name in new[] { "embedded_border", "embedded_unfocused_border" })
        {
            using var border = (StyleBoxFlat)ThemeDB.GetDefaultTheme().GetStylebox(name, "Window").Duplicate();
            border.ExpandMarginTop = height;
            theme.SetStylebox(name, "Window", border);
        }
    }

    private void ApplyOverrides(Node node, (StringName Name, int Size)[] sizes)
    {
        foreach (var (name, size) in sizes)
            node.Set(name, size > 0 ? ScaleSize(size) : size);
        if (node is BpmLabel label)
            label.ResetSize();
    }

    private int ScaleSize(int size) => (int)Math.Round(size * scale, MidpointRounding.AwayFromZero);
}
