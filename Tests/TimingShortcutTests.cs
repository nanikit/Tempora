namespace Tempora.Tests;

using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using Tempora.Classes.Visual.AudioDisplay;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class TimingShortcutTests
{
    [TestCase]
    public async Task Users_can_edit_timing_with_shortcuts_see_why_BPM_cannot_change_and_use_a_remembered_language_across_the_app()
    {
        // A sized viewport supplies cursor positions even when Godot runs headlessly.
        var viewport = new SubViewport { Size = new Vector2I(1280, 720) };
        viewport.AddChild(ResourceLoader.Load<PackedScene>("res://Classes/Visual/Main.tscn").Instantiate());
        using ISceneRunner runner = ISceneRunner.Load(viewport, autoFree: true);
        var container = (AudioVisualsContainer)runner.FindChild("AudioVisualsContainer")!;
        await runner.AwaitIdleFrame();

        bool roundBpm = Settings.Instance.RoundBPM;
        int rows = Settings.Instance.NumberOfRows;
        AudioFile originalAudio = Project.Instance.AudioFile;
        Timing originalTiming = Timing.Instance;
        Timing timing = AutoFree(new Timing { IsInstantiating = true })!;
        Timing.Instance = timing;
        Settings.Instance.RoundBPM = false;
        timing.AddTimingPoint(0d, 0d, 0.5d);
        timing.AddTimingPoint(0.35d, 0.7d, 0.5d);
        TimingPoint point = timing.TimingPoints[1];
        MementoHandler.Instance.ResetTimingHistory();
        container.UpdateBlocksScroll();
        await runner.AwaitIdleFrame();
        AudioDisplayPanel panel = container.AudioBlocks[0].AudioDisplayPanel;
        panel.UpdateTimingPointsIndices();
        panel.UpdateVisuals();
        Vector2 cursor = panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0.35f), panel.Size.Y / 2);
        SendInput(viewport, new InputEventMouseMotion { Position = cursor });
        await runner.AwaitInputProcessed();

        try
        {
            PressKey(viewport, Key.Ctrl, true);
            var message = (Label)runner.FindChild("MessageLabel")!;
            Vector2 earlierCursor = panel.GlobalPosition + new Vector2(panel.MeasurePositionToXPosition(0), panel.Size.Y / 2);
            double earlierBpm = timing.TimingPoints[0].Bpm;
            Wheel(viewport, earlierCursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(message.Visible).IsTrue();
            AssertThat(message.Text).IsEqual(TranslationServer.Translate("Can only change BPM of last timing point.").ToString());
            AssertThat(message.AnchorTop).IsEqual(0f);
            AssertThat(timing.TimingPoints[0].Bpm).IsEqual(earlierBpm);
            await runner.AwaitMillis(600);
            SendInput(viewport, new InputEventMouseMotion { Position = earlierCursor });
            Pan(viewport, earlierCursor, -1f);
            await runner.AwaitInputProcessed();
            await runner.AwaitMillis(600);
            AssertThat(message.Visible).IsTrue();
            AssertThat(timing.TimingPoints[0].Bpm).IsEqual(earlierBpm);
            await runner.AwaitMillis(550);
            AssertThat(message.Visible).IsFalse();

            SendInput(viewport, new InputEventMouseMotion { Position = cursor });

            Wheel(viewport, cursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(121d, 1e-6);
            AssertThat(point.Offset).IsEqual(0.7d);

            PressKey(viewport, Key.Alt, true);
            Wheel(viewport, cursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(126d, 1e-6);

            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, true);
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(125.9d, 1e-6);
            PressKey(viewport, Key.Ctrl, false);
            Wheel(viewport, cursor, MouseButton.WheelUp);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(0.702d, 1e-8);
            AssertThat(timing.TimingPoints[0].Offset).IsEqual(0d);
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(0);

            PressKey(viewport, Key.Alt, true);
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(0.692d, 1e-8);
            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, false);

            // Holding the existing point follows the same Ctrl/Shift roles as the wheel.
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left, Pressed = true });
            PressKey(viewport, Key.Ctrl, true);
            await runner.AwaitInputProcessed();
            AssertThat(Context.Instance.HeldTimingPoint).IsEqual(point);
            double bpmBeforeDrag = point.Bpm;
            cursor += new Vector2(20, 0);
            SendInput(viewport, new InputEventMouseMotion { Position = cursor, Relative = new Vector2(20, 0) });
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsGreater(bpmBeforeDrag);
            AssertThat(point.Offset).IsEqualApprox(0.692d, 1e-8);
            PressKey(viewport, Key.Ctrl, false);
            PressKey(viewport, Key.Shift, true);
            bpmBeforeDrag = point.Bpm;
            cursor += new Vector2(20, 0);
            SendInput(viewport, new InputEventMouseMotion { Position = cursor, Relative = new Vector2(20, 0) });
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsLess(0.692d);
            AssertThat(point.Bpm).IsEqualApprox(bpmBeforeDrag, 1e-6);
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left });
            PressKey(viewport, Key.Shift, false);
            await runner.AwaitInputProcessed();

            // Trackpad scrolling uses the same roles and wheel direction.
            PressKey(viewport, Key.Ctrl, true);
            bpmBeforeDrag = point.Bpm;
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox((int)bpmBeforeDrag + 1d, 1e-6);
            PressKey(viewport, Key.Alt, true);
            Pan(viewport, cursor, 1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox((int)bpmBeforeDrag - 4d, 1e-6);
            PressKey(viewport, Key.Alt, false);
            PressKey(viewport, Key.Shift, true);
            double bpmBeforePan = point.Bpm;
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Bpm).IsEqualApprox(bpmBeforePan + 0.1d, 1e-6);

            // Selecting both points retains batch offset adjustment.
            PressKey(viewport, Key.A, true);
            PressKey(viewport, Key.A, false);
            PressKey(viewport, Key.Ctrl, false);
            double offsetBeforePan = point.Offset;
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(offsetBeforePan + 0.002d, 1e-8);
            AssertThat(timing.TimingPoints[0].Offset).IsEqualApprox(0.002d, 1e-8);
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(0);
            PressKey(viewport, Key.Alt, true);
            Pan(viewport, cursor, 1f);
            await runner.AwaitInputProcessed();
            AssertThat(point.Offset).IsEqualApprox(offsetBeforePan - 0.008d, 1e-8);
            AssertThat(timing.TimingPoints[0].Offset).IsEqualApprox(-0.008d, 1e-8);
            PressKey(viewport, Key.Shift, false);

            // A long bundled song keeps both scroll sizes away from the timeline ends.
            Project.Instance.AudioFile = new AudioFile(ResourceLoader.Load<AudioStreamMP3>("res://Audio/Loop.mp3"));
            Settings.Instance.NumberOfRows = 1;
            container.UpdateNumberOfVisibleBlocks();
            await runner.AwaitIdleFrame();
            cursor = panel.GlobalPosition + new Vector2(panel.Size.X / 2, panel.Size.Y / 2);
            SendInput(viewport, new InputEventMouseMotion { Position = cursor });
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(5);
            Pan(viewport, cursor, -1f);
            await runner.AwaitInputProcessed();
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(0);
            PressKey(viewport, Key.Alt, false);
            Wheel(viewport, cursor, MouseButton.WheelDown);
            await runner.AwaitInputProcessed();
            AssertThat(container.NominalMeasurePositionStartForTopBlock).IsEqual(1);
            await VerifyAppLanguages(runner);
        }
        finally
        {
            PressKey(viewport, Key.Ctrl, false);
            PressKey(viewport, Key.Shift, false);
            PressKey(viewport, Key.Alt, false);
            SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = MouseButton.Left });
            Context.Instance.HeldTimingPoint = null;
            TimingPointSelection.Instance.DeselectAll();
            Timing.Instance = originalTiming;
            Settings.Instance.RoundBPM = roundBpm;
            Settings.Instance.NumberOfRows = rows;
            Project.Instance.AudioFile = originalAudio;
        }
    }

    private static async Task VerifyAppLanguages(ISceneRunner runner)
    {
        string originalSettings = FileAccess.GetFileAsString("user://settings.txt");
        string originalLocale = TranslationServer.GetLocale();
        string originalLanguage = Settings.Instance.Language;
        try
        {
            var language = (PopupMenu)runner.FindChild("LanguageMenu")!;
            var options = (PopupMenu)runner.FindChild("Options")!;
            var file = (PopupMenu)runner.FindChild("File")!;
            var help = (Window)runner.FindChild("HelpWindow")!;
            var body = help.GetNode<RichTextLabel>("RichTextLabel");
            var export = (Window)runner.FindChild("ExportWindow")!;
            var visual = (Window)runner.FindChild("VisualSettingsWindow")!;
            var grid = (Control)runner.FindChild("GridScrollBar")!;
            var gridTitle = grid.GetNode<Label>("HScrollBar/HBoxContainer/InsideTitleLabel");
            var message = (Label)runner.FindChild("MessageLabel")!;
            Project.Instance.NotificationMessage = "Saved visual options.";
            ProjectFileManager.Instance.SaveBeatSaberFileDialogPopup();
            var saveDialog = ProjectFileManager.Instance.SaveFileDialog;
            string[] headings = ["Getting started", "시작하기", "はじめに", "开始使用", "開始使用"];
            string[] newProject = ["New Project", "새 프로젝트", "新規プロジェクト", "新建项目", "新增專案"];
            AssertThat(options.GetItemSubmenuNode(options.ItemCount - 1)).IsEqual(language);
            for (int i = 0; i < headings.Length; i++)
            {
                language.EmitSignal(PopupMenu.SignalName.IdPressed, i + 1);
                await runner.AwaitIdleFrame();
                AssertThat(body.GetParsedText()).Contains(headings[i]);
                AssertThat(file.Tr(file.GetItemText(0)).ToString()).IsEqual(newProject[i]);
                AssertThat(gridTitle.Text).IsEqual(grid.Tr("Grid") + ":");
                AssertThat(message.Text).IsEqual(message.Tr("Saved visual options.").ToString());
                AssertThat(saveDialog.Title).IsEqual(string.Format(saveDialog.Tr("Export Beat Saber (v{0})"), Settings.Instance.BeatSaberExportFormat));
                if (i > 0)
                {
                    var exportLabel = export.FindChild("RemovePointsThatChangeNothing").GetNode<Label>("Label");
                    var visualLabel = visual.FindChild("stepSize").GetNode<Label>("Label");
                    AssertThat(exportLabel.Tr(exportLabel.Text).ToString()).IsNotEqual(exportLabel.Text);
                    AssertThat(visualLabel.Tr(visualLabel.Text).ToString()).IsNotEqual(visualLabel.Text);
                    AssertThat(message.Text).IsNotEqual("Saved visual options.");
                }
                for (int item = 0; item < language.ItemCount; item++)
                    AssertThat(language.IsItemChecked(item)).IsEqual(item == i + 1);

                foreach (int width in new[] { 640, 520 })
                    foreach (Window window in new[] { export, visual })
                    {
                        window.Size = new Vector2I(width, window.Size.Y);
                        window.Popup();
                        var tabs = (TabContainer)window.FindChild("TabContainer");
                        for (int tab = 0; tab < tabs.GetTabCount(); tab++)
                        {
                            tabs.CurrentTab = tab;
                            await runner.AwaitIdleFrame();
                            await runner.AwaitIdleFrame();
                            Vector2 minimum = window.GetNode<Control>("Margin").GetCombinedMinimumSize();
                            AssertThat(minimum.X).IsLessEqual((float)window.Size.X);
                            AssertThat(minimum.Y).IsLessEqual((float)window.Size.Y);
                            foreach (Node child in tabs.GetCurrentTabControl().GetChildren())
                            {
                                if (child is Label heading && heading.Visible)
                                    AssertThat(heading.Size.Y).IsLessEqual(heading.GetMinimumSize().Y + 1f);
                                if (child is HBoxContainer row)
                                {
                                    var label = row.GetNode<Label>("Label");
                                    var input = (Control)row.GetChild(0);
                                    float textStart = label.Position.X + label.GetCharacterBounds(0).Position.X;
                                    AssertThat(textStart - input.GetRect().End.X).IsBetween(0f, 16f);
                                }
                            }
                        }
                        window.Hide();
                    }
            }

            const string path = "C:/music/{test}/曲.tmpr";
            Project.Instance.ShowNotification("Saved to {0}", arguments: [path]);
            AssertThat(message.Text).Contains(path);
            string priorMessage = message.Text;
            await runner.AwaitMillis(600);
            double remaining = message.GetNode<Timer>("Timer").TimeLeft;
            TranslationServer.SetLocale("ko");
            await runner.AwaitIdleFrame();
            AssertThat(message.Text).Contains(path).IsNotEqual(priorMessage);
            AssertThat(message.GetNode<Timer>("Timer").TimeLeft).IsLessEqual(remaining);

            TranslationServer.SetLocale("en");
            Settings.Instance.LoadSettings();
            await runner.AwaitIdleFrame();
            AssertThat(body.GetParsedText()).Contains("開始使用");

            TranslationServer.SetLocale("zh_HK");
            await runner.AwaitIdleFrame();
            AssertThat(body.GetParsedText()).Contains("開始使用");

            TranslationServer.SetLocale("fr");
            await runner.AwaitIdleFrame();
            AssertThat(body.GetParsedText()).Contains("Getting started");
            language.EmitSignal(PopupMenu.SignalName.IdPressed, 0);
            AssertThat(TranslationServer.GetLocale()).IsEqual(TranslationServer.StandardizeLocale(OS.GetLocale()));
        }
        finally
        {
            ProjectFileManager.Instance.SaveFileDialog.Hide();
            Settings.Instance.Language = originalLanguage;
            TranslationServer.SetLocale(originalLocale);
            using var settingsFile = FileAccess.Open("user://settings.txt", FileAccess.ModeFlags.Write);
            settingsFile.StoreString(originalSettings);
        }
    }

    private static void PressKey(SubViewport viewport, Key key, bool pressed)
        => SendInput(viewport, new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private static void Wheel(SubViewport viewport, Vector2 cursor, MouseButton direction)
        => SendInput(viewport, new InputEventMouseButton { Position = cursor, ButtonIndex = direction, Pressed = true });

    private static void Pan(SubViewport viewport, Vector2 cursor, float amount)
        => SendInput(viewport, new InputEventPanGesture { Position = cursor, Delta = new Vector2(0, amount) });

    private static void SendInput(SubViewport viewport, InputEvent inputEvent)
    {
        // Global key state and viewport dispatch are separate native input boundaries.
        Input.ParseInputEvent(inputEvent);
        Input.FlushBufferedEvents();
        viewport.PushInput(inputEvent);
    }
}