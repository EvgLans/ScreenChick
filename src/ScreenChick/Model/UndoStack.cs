namespace ScreenChick.Model;

public sealed class UndoStack<T>
{
    private readonly Stack<T> _undo = new();
    private readonly Stack<T> _redo = new();

    public UndoStack(T initial) => Current = initial;

    public T Current { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Apply(T next)
    {
        _undo.Push(Current);
        _redo.Clear();
        Current = next;
    }

    /// <summary>Changes the current step without adding one, so a mark and its comment undo together.</summary>
    public void ReplaceCurrent(T next) => Current = next;

    /// <summary>Forgets the current step entirely, leaving nothing to redo.</summary>
    public void DropCurrent()
    {
        if (CanUndo) Current = _undo.Pop();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _redo.Push(Current);
        Current = _undo.Pop();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _undo.Push(Current);
        Current = _redo.Pop();
    }
}
