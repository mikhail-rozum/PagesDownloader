namespace PagesDownloader.Library;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Contains extension methods for <see cref="Uri"/>.
/// </summary>
internal static class UriExtensions
{
    /// <summary>
    /// Returns key of HTTP request for URL (scheme, host, port, path and query).
    /// </summary>
    /// <param name="url">URL.</param>
    /// <returns>Request key.</returns>
    public static string GetRequestKey(this Uri url)
    {
        return url.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }

    /// <summary>
    /// Returns file name for URL (host, path and 8 characters of URL hash).
    /// </summary>
    /// <param name="url">URL.</param>
    /// <returns>File name.</returns>
    public static string ToFileName(this Uri url)
    {
        var path = Uri.UnescapeDataString(url.AbsolutePath);
        var name = path.Trim('/').Replace('/', '_');
        if (path.EndsWith('/'))
        {
            name = name.Length == 0 ? "index" : $"{name}_index";
        }

        var extension = Path.GetExtension(path);
        var isHtml = extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        if (isHtml)
        {
            name = name[..^extension.Length];
        }
        else
        {
            extension = ".html";
        }

        var readable = $"{url.Host}_{name}";
        if (readable.Length > 100)
        {
            readable = readable[..100];
        }

        var fileName = $"{readable}_{GetHash(url)}{extension}";
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        return fileName;
    }

    private static string GetHash(Uri url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(GetRequestKey(url)));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}
