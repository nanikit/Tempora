using System;
using Tempora.Classes.Utility;

namespace Tempora.Classes.TimingClasses;

public sealed class TimingEditSession(Timing timing)
{
    private IMemento? beforeEdit;
    public event Action<object?>? Committed;
    public event Action? Finished;
    public bool IsEditing => beforeEdit != null;

    public void Begin()
    {
        beforeEdit ??= timing.GetMemento();
    }

    public void Apply(Action operation, object? historyGroup = null)
    {
        bool finishHere = !IsEditing;
        Begin();
        try
        {
            operation();
        }
        catch
        {
            Cancel();
            throw;
        }
        if (finishHere)
            Commit(historyGroup);
    }

    public void Commit(object? historyGroup = null)
    {
        if (beforeEdit == null)
            return;
        bool changed = !timing.Matches(beforeEdit);
        beforeEdit = null;
        if (changed)
            Committed?.Invoke(historyGroup);
        Finished?.Invoke();
    }

    public void Cancel()
    {
        if (beforeEdit == null)
            return;
        IMemento original = beforeEdit;
        beforeEdit = null;
        timing.RestoreMemento(original);
        Finished?.Invoke();
    }
}