namespace PagesDownloader.Tests.Fakes;

using System.Collections.Concurrent;
using PagesDownloader.Library;

/// <summary>
/// Fake IFileService implementation.
/// </summary>
internal class FakeFileService : IFileService
{
    public ConcurrentDictionary<string, string> Files { get; } = new ();
    public ConcurrentQueue<string> CreatedDirectories { get; } = new ();

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        this.CreatedDirectories.Enqueue(path);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content);
        this.Files[path] = await reader.ReadToEndAsync(cancellationToken);
    }
}
