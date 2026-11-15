namespace PagesDownloader.Library;

/// <summary>
/// Download result.
/// </summary>
/// <param name="Url">URL.</param>
/// <param name="Status">Status.</param>
/// <param name="FilePath">Path to saved file (null if download failed).</param>
/// <param name="Error">Error message (null if download succeeded).</param>
public sealed record DownloadResult(Uri Url, DownloadStatus Status, string? FilePath = null, string? Error = null);
