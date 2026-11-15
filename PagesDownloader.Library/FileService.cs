namespace PagesDownloader.Library;

/// <summary>
/// Default IFileService implementation.
/// </summary>
public class FileService : IFileService
{
    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            });
            await content.CopyToAsync(file, cancellationToken);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }
}
