using Godot;
using Tempora.Classes.Utility;
using System;
using Tempora.Classes.TimingClasses;

public partial class ClearAllConfirmationDialog : ConfirmationDialog
{
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        Confirmed += OnClearAllConfirmationDialogConfirmed;
    }

    private void OnClearAllConfirmationDialogConfirmed()
    {
        Project.Instance.Timing.DeleteAllTimingPoints();
        Project.Instance.Timing.DeleteAllTimeSignaturePoints();
    }
}