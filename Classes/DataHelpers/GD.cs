using System.Diagnostics;

namespace Tempora.Classes.DataHelpers;

public static class GD
{
    public static void Print(string what)
    {
        Debugger.Log(2, "GD.Print()", what);
        Godot.GD.Print(what);
    }
}
