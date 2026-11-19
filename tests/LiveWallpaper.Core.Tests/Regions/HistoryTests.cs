using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Regions;

public sealed class HistoryTests
{
    [Fact]
    public void Undo_ReturnsToPreviousState()
    {
        var history = new History<int>(0);
        history.Push(1);
        history.Push(2);

        Assert.Equal(1, history.Undo());
        Assert.Equal(1, history.Current);
    }

    [Fact]
    public void Redo_RestoresUndoneState()
    {
        var history = new History<int>(0);
        history.Push(1);
        history.Undo();

        Assert.Equal(1, history.Redo());
    }

    [Fact]
    public void PushAfterUndo_ClearsRedo()
    {
        var history = new History<int>(0);
        history.Push(1);
        history.Undo();
        history.Push(5);

        Assert.False(history.CanRedo);
        Assert.Equal(5, history.Redo());
    }

    [Fact]
    public void UndoAtStart_StaysPut()
    {
        var history = new History<string>("a");

        Assert.False(history.CanUndo);
        Assert.Equal("a", history.Undo());
    }

    [Fact]
    public void History_KeepsAtMostOneHundredEntries()
    {
        var history = new History<int>(0);
        for (var i = 1; i <= 150; i++)
        {
            history.Push(i);
        }

        var steps = 0;
        while (history.CanUndo)
        {
            history.Undo();
            steps++;
        }

        Assert.Equal(99, steps);
        Assert.Equal(51, history.Current);
    }
}
