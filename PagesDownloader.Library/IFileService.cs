namespace PagesDownloader.Library;

/// <summary>
/// File service.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Creates a directory.
    /// </summary>
    /// <param name="path">Directory path.</param>
    void CreateDirectory(string path);

    /// <summary>
    /// Creates a file with specified content. Deletes the file if writing fails.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="content">File content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(string path, Stream content, CancellationToken cancellationToken);
}
