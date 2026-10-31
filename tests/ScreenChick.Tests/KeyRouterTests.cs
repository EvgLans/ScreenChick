using ScreenChick.Input;
using Xunit;

namespace ScreenChick.Tests;

public class KeyRouterTests
{
    private const int VkA = 0x41;

    private static KeyInput Down(int vk, KeyMods mods = KeyMods.None) => new(vk, true, mods, false);
    private static KeyInput Up(int vk) => new(vk, false, KeyMods.None, false);

    private static KeyRouter Router(PasteDecision decision = PasteDecision.Trigger) => new(() => decision);

    [Fact]
    public void Print_screen_starts_a_capture_and_is_hidden_from_other_apps()
    {
        var result = Router().Process(Down(Hotkey.VkPrintScreen));

        Assert.True(result.Swallow);
        Assert.Equal(KeyAction.Capture, result.Action);
    }

    [Fact]
    public void Alt_print_screen_is_left_to_windows()
    {
        var result = Router().Process(Down(Hotkey.VkPrintScreen, KeyMods.Alt));

        Assert.False(result.Swallow);
    }

    [Fact]
    public void Custom_hotkey_replaces_the_default()
    {
        var router = Router();
        router.Hotkey = new Hotkey(VkA, KeyMods.Ctrl | KeyMods.Shift);

        Assert.Equal(KeyAction.Capture, router.Process(Down(VkA, KeyMods.Ctrl | KeyMods.Shift)).Action);
        Assert.False(router.Process(Down(Hotkey.VkPrintScreen)).Swallow);
    }

    [Fact]
    public void Ctrl_v_triggers_paste_when_armed()
    {
        var result = Router().Process(Down(KeyRouter.VkV, KeyMods.Ctrl));

        Assert.True(result.Swallow);
        Assert.Equal(KeyAction.Paste, result.Action);
    }

    [Fact]
    public void Ctrl_v_passes_through_when_nothing_is_queued()
    {
        Assert.False(Router(PasteDecision.Pass).Process(Down(KeyRouter.VkV, KeyMods.Ctrl)).Swallow);
    }

    [Theory]
    [InlineData(KeyMods.None)]
    [InlineData(KeyMods.Ctrl | KeyMods.Shift)]
    [InlineData(KeyMods.Ctrl | KeyMods.Alt)]
    public void Only_plain_ctrl_v_is_taken(KeyMods mods)
    {
        Assert.False(Router().Process(Down(KeyRouter.VkV, mods)).Swallow);
    }

    [Fact]
    public void Our_own_keystrokes_are_never_intercepted()
    {
        var router = Router();

        var result = router.Process(new KeyInput(KeyRouter.VkV, true, KeyMods.Ctrl, IsOwnInjected: true));

        Assert.False(result.Swallow);
        Assert.Equal(KeyAction.None, result.Action);
    }

    [Fact]
    public void Holding_ctrl_v_triggers_once_and_repeats_stay_swallowed_until_release()
    {
        var decisions = new Queue<PasteDecision>([PasteDecision.Trigger, PasteDecision.Pass]);
        var router = new KeyRouter(decisions.Dequeue);

        Assert.Equal(KeyAction.Paste, router.Process(Down(KeyRouter.VkV, KeyMods.Ctrl)).Action);

        var repeat = router.Process(Down(KeyRouter.VkV, KeyMods.Ctrl));
        Assert.True(repeat.Swallow);
        Assert.Equal(KeyAction.None, repeat.Action);

        Assert.True(router.Process(Up(KeyRouter.VkV)).Swallow);

        Assert.False(router.Process(Down(KeyRouter.VkV, KeyMods.Ctrl)).Swallow);
    }

    [Fact]
    public void Ctrl_v_during_a_paste_is_swallowed_without_starting_another()
    {
        var result = Router(PasteDecision.Swallow).Process(Down(KeyRouter.VkV, KeyMods.Ctrl));

        Assert.True(result.Swallow);
        Assert.Equal(KeyAction.None, result.Action);
    }

    [Fact]
    public void Recording_reports_the_next_chord_instead_of_acting()
    {
        var router = Router();
        router.BeginRecording();

        var result = router.Process(Down(Hotkey.VkPrintScreen, KeyMods.Shift));

        Assert.Equal(KeyAction.Recorded, result.Action);
        Assert.True(result.Swallow);
        Assert.Equal(new Hotkey(Hotkey.VkPrintScreen, KeyMods.Shift), router.LastRecorded);
    }

    [Fact]
    public void Recording_waits_past_bare_modifiers()
    {
        var router = Router();
        router.BeginRecording();

        Assert.False(router.Process(Down(0xA2, KeyMods.Ctrl)).Swallow);
        Assert.Equal(KeyAction.Recorded, router.Process(Down(VkA, KeyMods.Ctrl)).Action);
    }

    [Fact]
    public void Ordinary_typing_is_untouched()
    {
        var router = Router();

        Assert.False(router.Process(Down(VkA)).Swallow);
        Assert.False(router.Process(Up(VkA)).Swallow);
    }
}
