using System.Windows.Input;

namespace ScreenChick.Input;

[Flags]
public enum KeyMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Win = 8 }

public sealed record Hotkey(int Vk, KeyMods Mods)
{
    public const int VkPrintScreen = 0x2C;
    public static readonly Hotkey Default = new(VkPrintScreen, KeyMods.None);

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (Mods.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
            if (Mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
            if (Mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
            if (Mods.HasFlag(KeyMods.Win)) parts.Add("Win");
            parts.Add(Vk == VkPrintScreen ? "Print Screen" : KeyInterop.KeyFromVirtualKey(Vk).ToString());
            return string.Join("+", parts);
        }
    }
}

public enum PasteDecision { Pass, Trigger, Swallow }

public enum KeyAction { None, Capture, Paste, Recorded }

public readonly record struct KeyInput(int Vk, bool IsDown, KeyMods Mods, bool IsOwnInjected);

public readonly record struct KeyResult(bool Swallow, KeyAction Action);

/// <summary>
/// Decides what happens to each key press system-wide. Runs on the hook thread and must stay fast.
/// A key swallowed on press stays swallowed until released, so auto-repeat from a held Ctrl+V
/// cannot leak through or paste a second time.
/// </summary>
public sealed class KeyRouter(Func<PasteDecision> decidePaste)
{
    public const int VkV = 0x56;
    public const int VkEscape = 0x1B;

    private readonly HashSet<int> _held = [];
    private volatile Hotkey _hotkey = Hotkey.Default;
    private volatile bool _recording;
    private volatile Hotkey? _recorded;

    public Hotkey Hotkey
    {
        get => _hotkey;
        set => _hotkey = value;
    }

    public Hotkey? LastRecorded => _recorded;

    /// <summary>The next key chord is reported as Recorded instead of acting, Print Screen included.</summary>
    public void BeginRecording() => _recording = true;
    public void CancelRecording() => _recording = false;

    public KeyResult Process(KeyInput key)
    {
        if (key.IsOwnInjected) return default;

        if (!key.IsDown) return new KeyResult(_held.Remove(key.Vk), KeyAction.None);

        if (_held.Contains(key.Vk)) return new KeyResult(true, KeyAction.None);

        if (IsModifier(key.Vk)) return default;

        if (_recording)
        {
            _recording = false;
            _recorded = new Hotkey(key.Vk, key.Mods);
            return Swallow(key.Vk, KeyAction.Recorded);
        }

        var hotkey = _hotkey;
        if (key.Vk == hotkey.Vk && key.Mods == hotkey.Mods) return Swallow(key.Vk, KeyAction.Capture);

        if (key.Vk == VkV && key.Mods == KeyMods.Ctrl)
        {
            switch (decidePaste())
            {
                case PasteDecision.Trigger: return Swallow(key.Vk, KeyAction.Paste);
                case PasteDecision.Swallow: return Swallow(key.Vk, KeyAction.None);
            }
        }

        return default;
    }

    private KeyResult Swallow(int vk, KeyAction action)
    {
        _held.Add(vk);
        return new KeyResult(true, action);
    }

    private static bool IsModifier(int vk) => vk is
        0x10 or 0x11 or 0x12 or       // Shift, Ctrl, Alt
        0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or // left and right variants
        0x5B or 0x5C;                 // Win
}
