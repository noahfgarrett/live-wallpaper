namespace LiveWallpaper.Windows.Tests;

[Collection(MessageWindowCollection.Name)]
public sealed class EngineTakeoverTests
{
    private static readonly TimeSpan NoGrace = TimeSpan.Zero;
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private static string UniqueName() => $@"Local\LiveWallpaper.Tests.{Guid.NewGuid():N}";

    [Fact]
    public void TryTakeOver_NothingRunning_AcquiresWithoutAsking()
    {
        WindowsOnly.Require();
        var asked = false;

        using var taken = EngineTakeover.TryTakeOver(UniqueName(), () => asked = true, NoGrace, CloseTimeout, () => Assert.Fail("Nothing should be stopped."));

        Assert.NotNull(taken);
        Assert.False(asked);
    }

    [Fact]
    public void TryTakeOver_OldEngineClosesWhenAsked_Acquires()
    {
        WindowsOnly.Require();
        var name = UniqueName();
        using var old = new OldEngine(name, releasesOnClose: true);

        using var taken = EngineTakeover.TryTakeOver(name, () => false, NoGrace, CloseTimeout, () => Assert.Fail("The old engine closed by itself."));

        Assert.NotNull(taken);
        Assert.Equal(1, old.CloseRequests);
    }

    [Fact]
    public void TryTakeOver_CurrentEngineAnswers_ClosesNothing()
    {
        WindowsOnly.Require();
        var name = UniqueName();
        using var old = new OldEngine(name, releasesOnClose: true);

        var taken = EngineTakeover.TryTakeOver(name, () => true, NoGrace, CloseTimeout, () => Assert.Fail("A current engine must not be stopped."));

        Assert.Null(taken);
        Assert.Equal(0, old.CloseRequests);
        Assert.Null(SingleInstance.TryAcquire(name));
    }

    [Fact]
    public void TryTakeOver_OldEngineIgnoresClose_ForceStopsIt()
    {
        WindowsOnly.Require();
        var name = UniqueName();
        using var old = new OldEngine(name, releasesOnClose: false);
        var forced = false;

        using var taken = EngineTakeover.TryTakeOver(name, () => false, NoGrace, TimeSpan.FromMilliseconds(500), () =>
        {
            forced = true;
            old.Release();
        });

        Assert.True(forced);
        Assert.NotNull(taken);
    }

    /// <summary>Stands in for an engine without a pipe: holds the mutex and pumps a message window on its own thread.</summary>
    private sealed class OldEngine : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private readonly bool _releasesOnClose;
        private nint _window;
        private volatile bool _releaseNext;
        private int _closeRequests;

        public OldEngine(string mutexName, bool releasesOnClose)
        {
            _releasesOnClose = releasesOnClose;
            _thread = new Thread(() => Run(mutexName)) { IsBackground = true };
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(5)), "The stand-in engine didn't start.");
        }

        public int CloseRequests => Volatile.Read(ref _closeRequests);

        /// <summary>Releases the mutex and exits, as if the process had been killed.</summary>
        public void Release()
        {
            _releaseNext = true;
            MessageWindow.PostClose(_window);
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)), "The stand-in engine didn't stop.");
        }

        public void Dispose()
        {
            if (_thread.IsAlive)
            {
                Release();
            }
            _ready.Dispose();
        }

        private void Run(string mutexName)
        {
            var instance = SingleInstance.TryAcquire(mutexName) ?? throw new InvalidOperationException("The test mutex was already taken.");
            using var window = new MessageWindow(e =>
            {
                if (e != MessageWindowEvent.CloseRequested)
                {
                    return;
                }
                if (!_releaseNext)
                {
                    Interlocked.Increment(ref _closeRequests);
                }
                if (_releaseNext || _releasesOnClose)
                {
                    instance.Dispose();
                    MessageWindow.Quit();
                }
            });
            _window = window.Handle;
            _ready.Set();
            window.RunMessageLoop();
        }
    }
}
