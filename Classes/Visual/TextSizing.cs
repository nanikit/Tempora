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
    private double scale = BaseScale;

    public TextSizing() => theme.DefaultFontSize = ScaleSize(16);

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
            window.Theme = theme;
        ApplyOverrides(node, overrides[node]);
    }

    public void Unregister(Node node) => overrides.Remove(node);

    public void ApplyScale(double systemScale, double userScale)
    {
        scale = BaseScale * systemScale * userScale;
        theme.DefaultFontSize = ScaleSize(16);
        foreach (var (node, sizes) in overrides.ToArray())
            ApplyOverrides(node, sizes);
    }

    public void Dispose() => theme.Dispose();

    private void ApplyOverrides(Node node, (StringName Name, int Size)[] sizes)
    {
        foreach (var (name, size) in sizes)
            node.Set(name, size > 0 ? ScaleSize(size) : size);
        if (node is BpmLabel label)
            label.ResetSize();
    }

    private int ScaleSize(int size) => (int)Math.Round(size * scale, MidpointRounding.AwayFromZero);
}