using System.Globalization;
using ScreenChick.Model;

namespace ScreenChick.Localization;

public enum UiLanguage { Auto, Russian, English }

/// <summary>Every piece of interface text, in Russian and English. XAML reads these through x:Static.</summary>
public static class L
{
    private static bool _russian = true;

    /// <summary>Open windows keep their text, so a caller that gets true has to recreate them.</summary>
    public static bool Use(UiLanguage language)
    {
        var russian = IsRussian(language, CultureInfo.CurrentUICulture);
        var changed = russian != _russian;
        _russian = russian;
        return changed;
    }

    public static bool IsRussian(UiLanguage language, CultureInfo system) =>
        language == UiLanguage.Russian || (language == UiLanguage.Auto && system.TwoLetterISOLanguageName == "ru");

    private static string T(string russian, string english) => _russian ? russian : english;

    public static string Shots(int n) => _russian
        ? MessageFormatter.Plural(n, "снимок", "снимка", "снимков")
        : n == 1 ? "1 shot" : $"{n} shots";

    public static string Comments(int n) => _russian
        ? MessageFormatter.Plural(n, "комментарий", "комментария", "комментариев")
        : n == 1 ? "1 comment" : $"{n} comments";

    public static string ShotHeading => T("Снимок", "Shot");

    // Tray and notifications
    public static string StartedTitle => T("Кря! ScreenChick запущен", "Quack! ScreenChick is running");
    public static string StartedBody(string hotkey) => T(
        $"{hotkey}: новый снимок. Значок в трее: настройки и выход.",
        $"{hotkey}: new shot. Tray icon: settings and exit.");
    public static string StripFull(int max) => T(
        $"В ленте уже {max} снимков. Убери лишние или очисти ленту.",
        $"The strip already has {max} shots. Remove some or clear it.");
    public static string PasteStoppedFocus => T(
        "Вставка остановлена: ты переключил окно. Снимки остались в ленте.",
        "Paste stopped because you switched windows. Your shots are still in the strip.");
    public static string PasteStoppedClipboard => T(
        "Буфер обмена занят другой программой. Снимки остались в ленте, попробуй ещё раз.",
        "Another program is using the clipboard. Your shots are still in the strip, so try again.");
    public static string MenuNewShot => T("Новый снимок", "New shot");
    public static string MenuClear => T("Очистить ленту", "Clear strip");
    public static string MenuSettings => T("Настройки…", "Settings…");
    public static string MenuExit => T("Выход", "Exit");

    // Strip
    public static string StripDrag => T("Перетащить", "Drag to move");
    public static string StripOpen => T("Открыть и поправить", "Open and edit");
    public static string StripRemove => T("Убрать снимок", "Remove shot");
    public static string StripNewShot => T("+ Новый снимок", "+ New shot");
    public static string StripPasteArmed => T("Вставить · Ctrl+V", "Paste · Ctrl+V");
    public static string StripPaste => T("Вставить", "Paste");
    public static string StripClear => T("Очистить", "Clear");
    public static string StripCopiedNotice => T(
        "Ты что-то скопировал, поэтому Ctrl+V вставит это. Снимки: кнопка выше.",
        "You copied something, so Ctrl+V pastes that. For shots, use the button above.");

    // Overlay
    public static string ToolMark => T("Метка · 1", "Mark · 1");
    public static string ToolMarkTip => T("Обвести или ткнуть и написать", "Circle or click a spot, then type");
    public static string ToolRedact => T("Замазать · 2", "Redact · 2");
    public static string ToolRedactTip => T(
        "Провести по тому, что нужно скрыть. Колесо мыши меняет толщину",
        "Drag over what to hide. The mouse wheel changes the thickness");
    public static string ToolCrop => T("Обрезать · 3", "Crop · 3");
    public static string ToolCropTip => T("Выделить часть снимка, которую оставить", "Select the part of the shot to keep");
    public static string UndoTip => T("Отменить (Ctrl+Z)", "Undo (Ctrl+Z)");
    public static string RedoTip => T("Вернуть (Ctrl+Y)", "Redo (Ctrl+Y)");
    public static string Done => T("Готово · Enter", "Done · Enter");
    public static string CancelEsc => T("Отмена · Esc", "Cancel · Esc");
    public static string DeleteMark => T("Удалить метку", "Delete mark");
    public static string EditorHint => T(
        "Enter: сохранить · Shift+Enter: новая строка · Esc: отмена",
        "Enter: save · Shift+Enter: new line · Esc: cancel");
    public static string DiscardWarning => T(
        "Правки пропадут. Нажми Esc ещё раз, чтобы выйти без сохранения",
        "Your edits will be lost. Press Esc again to exit without saving");

    // Settings
    public static string SettingsTitle => T("ScreenChick: настройки", "ScreenChick: settings");
    public static string LanguageLabel => T("Язык интерфейса", "Interface language");
    public static string LanguageAuto => T("Как в системе", "System default");
    public static string HotkeyLabel => T("Клавиша снимка", "Capture key");
    public static string HotkeyChange => T("Изменить", "Change");
    public static string HotkeyRecording => T("Нажми клавишу… (Esc: отмена)", "Press a key… (Esc: cancel)");
    public static string DelayLabel => T("Пауза между картинками при вставке", "Pause between images when pasting");
    public static string DelayHint => T(
        "Увеличь, если чат успевает принять не все снимки.",
        "Increase it if a chat doesn't accept all the images.");
    public static string Milliseconds(int n) => T($"{n} мс", $"{n} ms");
    public static string AutostartLabel => T("Запускать вместе с Windows", "Start with Windows");
    public static string Save => T("Сохранить", "Save");
    public static string Cancel => T("Отмена", "Cancel");
}
