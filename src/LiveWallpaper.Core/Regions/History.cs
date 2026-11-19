namespace LiveWallpaper.Core.Regions;

/// <summary>A bounded undo/redo stack, ported from the browser preview (prototype/history.js).</summary>
public sealed class History<T>
{
    public const int MaxEntries = 100;

    private readonly List<T> _states;
    private int _index;

    public History(T initial)
    {
        _states = [initial];
    }

    public T Current => _states[_index];

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _states.Count - 1;

    public void Push(T state)
    {
        _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        _states.Add(state);
        if (_states.Count > MaxEntries)
        {
            _states.RemoveAt(0);
        }
        _index = _states.Count - 1;
    }

    public T Undo()
    {
        if (CanUndo)
        {
            _index--;
        }
        return Current;
    }

    public T Redo()
    {
        if (CanRedo)
        {
            _index++;
        }
        return Current;
    }
}
