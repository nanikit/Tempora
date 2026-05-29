namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

/// <summary>
/// Singletons are registered as autoloads (.tscn) in project.godot.
/// Round-trip integration tests depend on them, so this pins that the autoloads are alive in the test runtime.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SingletonAutoloadTest
{
    [TestCase]
    public void Autoload_singletons_are_alive_in_test_runtime()
    {
        AssertThat(GlobalEvents.Instance).IsNotNull();
        AssertThat(Project.Instance).IsNotNull();
        AssertThat(Timing.Instance).IsNotNull();
        AssertThat(MusicPlayer.Instance).IsNotNull();
    }
}