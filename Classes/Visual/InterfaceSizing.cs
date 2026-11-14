using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Tempora.Classes.Visual;

public sealed class InterfaceSizing : IDisposable
{
    private readonly Dictionary<Window, (Vector2I Minimum, Vector2I Preferred, Vector2I Applied)> windows = new();
    private float scale = 1;
    private bool layoutQueued;
    private bool disposed;

    public void Register(Window window)
    {
        if (!windows.TryAdd(window, (window.MinSize, window.Size, window.Size)))
            return;
        window.VisibilityChanged += QueueLayout;
        if (window.GetParent() == null)
            window.SizeChanged += QueueLayout;
        ApplyWindowScale(window);
        if (window is FileDialog dialog)
            MakeFileContentScrollable(dialog);
        QueueLayout();
    }

    public void Unregister(Window window)
    {
        if (!windows.Remove(window))
            return;
        window.VisibilityChanged -= QueueLayout;
        if (window.GetParent() == null)
            window.SizeChanged -= QueueLayout;
    }

    public void ApplyScale(float value)
    {
        var positions = windows.Keys.Where(window => window.Visible && window.IsEmbedded())
            .Select(window => (Window: window, ScreenPosition: window.GetScreenTransform() * GetTitleOrigin(window))).ToArray();

        scale = value;
        foreach (Window window in windows.Keys)
            ApplyWindowScale(window);

        foreach (var (window, position) in positions)
            RestoreTitleScreenPosition(window, position);
        QueueLayout();
    }

    public void Dispose()
    {
        disposed = true;
        foreach (Window window in windows.Keys.ToArray())
            Unregister(window);
    }

    private void ApplyWindowScale(Window window)
    {
        // Embedded windows already inherit their embedder's canvas scale.
        window.ContentScaleFactor = window.GetParent() == null || !window.IsEmbedded() ? scale : 1;
    }

    private static void RestoreTitleScreenPosition(Window window, Vector2 position)
        => window.Position += (Vector2I)(window.GetScreenTransform().AffineInverse() * position - GetTitleOrigin(window)).Round();

    private static Vector2 GetTitleOrigin(Window window)
        => window.Borderless ? Vector2.Zero : new Vector2(0, -window.GetThemeConstant("title_height"));

    private void QueueLayout()
    {
        if (layoutQueued || disposed)
            return;
        layoutQueued = true;
        Callable.From(RefreshWindows).CallDeferred();
    }

    private void RefreshWindows()
    {
        layoutQueued = false;
        if (disposed)
            return;
        foreach (Window window in windows.Keys.ToArray())
        {
            if (!window.IsInsideTree() || window.GetParent() == null || !window.Visible)
                continue;
            ApplyWindowScale(window);
            FitWindow(window);
        }
    }

    private void FitWindow(Window window)
    {
        var original = windows[window];
        Vector2I preferred = window.Size == original.Applied ? original.Preferred : window.Size;
        Rect2I bounds = GetContentBounds(window);
        Vector2I maximum = bounds.Size;
        if (window is not Popup)
        {
            Vector2I position = window.Position.Clamp(bounds.Position, bounds.End - Vector2I.One);
            bounds = new Rect2I(position, bounds.End - position);
        }

        window.MinSize = Vector2I.Zero;
        window.MaxSize = maximum;
        window.MinSize = original.Minimum.Min(bounds.Size);

        Vector2I content = (Vector2I)(window.GetContentsMinimumSize() * window.ContentScaleFactor).Ceil();
        window.Size = (window is Popup ? content : preferred.Max(content)).Min(bounds.Size);
        window.Position = window.Position.Clamp(bounds.Position, bounds.End - window.Size);
        windows[window] = (original.Minimum, preferred, window.Size);
    }

    private static void MakeFileContentScrollable(FileDialog dialog)
    {
        VBoxContainer content = dialog.GetVBox();
        if (content.GetParent() != dialog)
            return;
        float footerHeight = ((Control)dialog.GetOkButton().GetParent()).GetCombinedMinimumSize().Y
            + dialog.GetThemeStylebox("panel").GetMinimumSize().Y + dialog.GetThemeConstant("buttons_separation");
        // ItemList has no minimum row height. Preserve the designed body height so
        // scrolling the dialog cannot collapse its file list to zero rows.
        content.CustomMinimumSize = new Vector2(0, Math.Max(0, dialog.Size.Y / dialog.ContentScaleFactor - footerHeight));
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        dialog.RemoveChild(content);
        var scroll = new ScrollContainer();
        scroll.AddChild(content);
        dialog.AddChild(scroll);
    }

    private static Rect2I GetContentBounds(Window window)
    {
        Rect2I bounds = GetParentBounds(window);
        int titleHeight = window.Borderless ? 0 : window.GetThemeConstant("title_height");
        bounds.Position += new Vector2I(0, titleHeight);
        bounds.Size = (bounds.Size - new Vector2I(0, titleHeight)).Max(Vector2I.One);
        return bounds;
    }

    private static Rect2I GetParentBounds(Window window)
    {
        if (!window.IsEmbedded())
            return DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
        for (Node parent = window.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is Viewport { GuiEmbedSubwindows: true } viewport)
                return (Rect2I)viewport.GetVisibleRect();
        }
        return (Rect2I)window.GetTree().Root.GetVisibleRect();
    }
}