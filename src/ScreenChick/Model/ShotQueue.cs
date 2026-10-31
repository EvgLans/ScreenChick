namespace ScreenChick.Model;

/// <summary>The strip of pending shots. A shot's letter is its position, so removing one relabels the rest.</summary>
public sealed class ShotQueue
{
    public const int MaxShots = 26;

    private readonly List<Shot> _shots = [];

    public event Action? Changed;

    public IReadOnlyList<Shot> Shots => _shots;
    public int Count => _shots.Count;
    public bool IsFull => _shots.Count >= MaxShots;

    public static char LetterOf(int index) => (char)('A' + index);
    public static string LabelOf(int shotIndex, int markIndex) => $"{LetterOf(shotIndex)}{markIndex + 1}";

    public bool Add(Shot shot)
    {
        if (IsFull) return false;
        _shots.Add(shot);
        Changed?.Invoke();
        return true;
    }

    public void RemoveAt(int index)
    {
        _shots.RemoveAt(index);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _shots.Clear();
        Changed?.Invoke();
    }

    public void NotifyEdited() => Changed?.Invoke();
}
