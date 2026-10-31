using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using ScreenChick.Input;
using ScreenChick.Localization;

namespace ScreenChick.Settings;

public sealed class AppSettings
{
    public const int MinPasteDelayMs = 100;
    public const int MaxPasteDelayMs = 2000;

    public UiLanguage Language { get; set; } = UiLanguage.Auto;
    public int HotkeyVk { get; set; } = Hotkey.VkPrintScreen;
    public KeyMods HotkeyMods { get; set; } = KeyMods.None;
    public int PasteDelayMs { get; set; } = 400;

    [JsonIgnore]
    public Hotkey Hotkey => new(HotkeyVk, HotkeyMods);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenChick", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenChick";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(ValueName, false);
        }
    }
}
