using LiveWallpaper.Core;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Library;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Media;

namespace LiveWallpaper.App.Services;

public sealed class LibraryService
{
    private readonly LibraryStore _store = new(AppPaths.LibraryDirectory, TimeProvider.System);

    public IReadOnlyList<LibraryItem> Load() => _store.Load();

    public string ImagePath(LibraryItem item) => _store.ImagePath(item);

    public string ThumbnailPath(LibraryItem item) => _store.ThumbnailPath(item);

    public void Update(LibraryItem item) => _store.Update(item);

    public bool Remove(Guid id) => _store.Remove(id);

    public async Task<IReadOnlyList<string>> ImportAsync(IEnumerable<string> paths)
    {
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            try
            {
                var size = await Task.Run(() => WicImageLoader.ReadSize(path)).ConfigureAwait(false);
                var item = _store.Import(path, size.Width, size.Height);
                try
                {
                    await Task.Run(() => ThumbnailWriter.Write(_store.ImagePath(item), _store.ThumbnailPath(item))).ConfigureAwait(false);
                }
                catch (ImageLoadException ex)
                {
                    Log.Error("app", "event=thumbnail-failed", ex);
                }
            }
            catch (ImageLoadException)
            {
                problems.Add($"{name} isn't an image Windows can read.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name} couldn't be copied into the library.");
                Log.Error("app", "event=import-failed", ex);
            }
        }
        return problems;
    }
}
