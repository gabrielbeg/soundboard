using Soundboard.Core.Hotkeys;

namespace Soundboard.Core.Tests;

public class HotkeyProcessorTests
{
    private const int A = 0x41;
    private const int B = 0x42;
    private const int F1 = 0x70;
    private const int Num1 = 0x61;
    private const int Num2 = 0x62;

    private readonly HotkeyProcessor _processor = new();
    private readonly List<Hotkey> _fired = [];

    public HotkeyProcessorTests()
    {
        _processor.Triggered += _fired.Add;
    }

    [Fact]
    public void SingleKey_Fires()
    {
        _processor.SetBindings([new Hotkey(F1)]);

        _processor.OnKeyDown(F1);

        Assert.Equal([new Hotkey(F1)], _fired);
    }

    [Fact]
    public void TwoKeyCombo_FiresRegardlessOfOrder()
    {
        _processor.SetBindings([new Hotkey(Num1, Num2)]);

        _processor.OnKeyDown(Num2);
        _processor.OnKeyDown(Num1);
        _processor.OnKeyUp(Num1);
        _processor.OnKeyUp(Num2);
        _processor.OnKeyDown(Num1);
        _processor.OnKeyDown(Num2);

        Assert.Equal(2, _fired.Count);
    }

    [Fact]
    public void LeftAndRightModifiers_AreEquivalent()
    {
        _processor.SetBindings([new Hotkey(VirtualKeys.Control, F1)]);

        _processor.OnKeyDown(VirtualKeys.RightControl);
        _processor.OnKeyDown(F1);

        Assert.Single(_fired);
    }

    [Fact]
    public void AutoRepeat_DoesNotRefire()
    {
        _processor.SetBindings([new Hotkey(F1)]);

        _processor.OnKeyDown(F1);
        _processor.OnKeyDown(F1);
        _processor.OnKeyDown(F1);

        Assert.Single(_fired);
    }

    [Fact]
    public void ExtraHeldKey_PreventsSingleKeyMatch()
    {
        _processor.SetBindings([new Hotkey(F1)]);

        _processor.OnKeyDown(VirtualKeys.LeftShift);
        _processor.OnKeyDown(F1);

        Assert.Empty(_fired);
    }

    [Fact]
    public void Suppression_SwallowsCompletingKeyDownAndUp()
    {
        _processor.SetBindings([new Hotkey(A, B)]);
        _processor.SuppressMatchedKeys = true;

        Assert.False(_processor.OnKeyDown(A));
        Assert.True(_processor.OnKeyDown(B));
        Assert.True(_processor.OnKeyUp(B));
        Assert.False(_processor.OnKeyUp(A));
    }

    [Fact]
    public void StaleKey_IsForgotten_WhenNotPhysicallyDown()
    {
        var physicallyDown = new HashSet<int>();
        var processor = new HotkeyProcessor(physicallyDown.Contains);
        var fired = new List<Hotkey>();
        processor.Triggered += fired.Add;
        processor.SetBindings([new Hotkey(F1)]);

        processor.OnKeyDown(A); // key-up for A is never delivered
        processor.OnKeyDown(F1);

        Assert.Equal([new Hotkey(F1)], fired);
    }

    [Fact]
    public void Recording_CapturesComboOnRelease()
    {
        Hotkey? recorded = null;
        _processor.BeginRecording(h => recorded = h);

        Assert.True(_processor.OnKeyDown(VirtualKeys.LeftControl));
        Assert.True(_processor.OnKeyDown(Num1));
        _processor.OnKeyUp(Num1);
        Assert.Null(recorded);
        _processor.OnKeyUp(VirtualKeys.LeftControl);

        Assert.Equal(new Hotkey(VirtualKeys.Control, Num1), recorded);
        Assert.False(_processor.IsRecording);
    }

    [Fact]
    public void Recording_EscapeCancels()
    {
        bool called = false;
        Hotkey? recorded = new Hotkey(A);
        _processor.BeginRecording(h => { called = true; recorded = h; });

        _processor.OnKeyDown(VirtualKeys.Escape);

        Assert.True(called);
        Assert.Null(recorded);
    }

    [Fact]
    public void Recording_DoesNotTriggerBindings()
    {
        _processor.SetBindings([new Hotkey(F1)]);
        _processor.BeginRecording(_ => { });

        _processor.OnKeyDown(F1);

        Assert.Empty(_fired);
    }

    [Fact]
    public void Hotkey_NormalizesOrder_ModifiersFirst()
    {
        Assert.Equal(new Hotkey(VirtualKeys.Control, A), new Hotkey(A, VirtualKeys.Control));
        Assert.Equal(VirtualKeys.Control, new Hotkey(A, VirtualKeys.Control).Key1);
    }
}
