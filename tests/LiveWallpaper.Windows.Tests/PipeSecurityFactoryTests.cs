using LiveWallpaper.Core.Ipc;

namespace LiveWallpaper.Windows.Tests;

public sealed class PipeSecurityFactoryTests
{
    [Fact]
    public async Task CurrentUserOnlyPipe_RoundTripsARequest()
    {
        WindowsOnly.Require();
        var name = $"lw-test-{Guid.NewGuid():N}";
        await using var server = new EnginePipeServer(name, PipeSecurityFactory.CurrentUserOnly(name), _ => Task.FromResult<EngineResponse>(new Failed("seen")));
        server.Start();

        var response = await new EngineClient(name).SendAsync(new GetStatus(), TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(new Failed("seen"), response);
    }

    [Fact]
    public void CurrentUserOnlyPipe_CanBeCreated()
    {
        WindowsOnly.Require();
        var name = $"lw-test-{Guid.NewGuid():N}";

        using var pipe = PipeSecurityFactory.CurrentUserOnly(name)();

        Assert.NotNull(pipe);
    }
}
