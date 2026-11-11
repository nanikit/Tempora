namespace Tempora.Tests;

using GdUnit4;
using Tempora.Classes.Audio;
using Tempora.Classes.TimingClasses;
using Tempora.Classes.Utility;
using static GdUnit4.Assertions;

/// <summary>
/// Round-trip integration tests depend on autoload services and the current timing data.
/// This pins that all required instances are available in the test runtime.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SingletonAutoloadTest
{
    [TestCase]
    public void Required_singletons_are_available_in_test_runtime()
    {
        AssertThat(GlobalEvents.Instance).IsNotNull();
        AssertThat(Project.Instance).IsNotNull();
        AssertThat(Timing.Instance).IsNotNull();
        AssertThat(MusicPlayer.Instance).IsNotNull();
    }
}