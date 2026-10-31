using System.Windows;
using ScreenChick.Input;
using ScreenChick.Localization;
using ScreenChick.Settings;

namespace ScreenChick.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly KeyRouter _router;
    private Hotkey _hotkey;

    public event Action? Saved;

    public SettingsWindow(AppSettings settings, KeyRouter router)
    {
        InitializeComponent();
        _settings = settings;
        _router = router;
        _hotkey = settings.Hotkey;

        LanguageBox.SelectedIndex = (int)settings.Language;
        HotkeyText.Text = _hotkey.Display;
        DelaySlider.Minimum = AppSettings.MinPasteDelayMs;
        DelaySlider.Maximum = AppSettings.MaxPasteDelayMs;
        DelaySlider.Value = settings.PasteDelayMs;
        AutostartBox.IsChecked = Autostart.IsEnabled;

        Closed += (_, _) => _router.CancelRecording();
    }

    public void OnHotkeyRecorded(Hotkey hotkey)
    {
        if (hotkey.Vk != KeyRouter.VkEscape) _hotkey = hotkey;
        HotkeyText.Text = _hotkey.Display;
        RecordButton.IsEnabled = true;
    }

    private void OnRecord(object sender, RoutedEventArgs e)
    {
        HotkeyText.Text = L.HotkeyRecording;
        RecordButton.IsEnabled = false;
        _router.BeginRecording();
    }

    private void OnDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DelayText != null) DelayText.Text = L.Milliseconds((int)e.NewValue);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.Language = (UiLanguage)LanguageBox.SelectedIndex;
        _settings.HotkeyVk = _hotkey.Vk;
        _settings.HotkeyMods = _hotkey.Mods;
        _settings.PasteDelayMs = (int)DelaySlider.Value;
        _settings.Save();
        Autostart.IsEnabled = AutostartBox.IsChecked == true;
        Saved?.Invoke();
        Close();
    }
}
