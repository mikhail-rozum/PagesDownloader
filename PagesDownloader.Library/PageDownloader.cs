namespace PagesDownloader.Library;

using Microsoft.Extensions.Logging;

/// <summary>
/// Implements web pages downloading.
/// </summary>
public class PageDownloader
{
    private readonly HttpClient httpClient;
    private readonly IFileService fileService;
    private readonly ILogger<PageDownloader> logger;

    /// <summary>
    /// ctor.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="fileService">File service.</param>
    /// <param name="logger">Logger.</param>
    public PageDownloader(HttpClient httpClient, IFileService fileService, ILogger<PageDownloader> logger)
    {
        this.httpClient = httpClient;
        this.fileService = fileService;
        this.logger = logger;
    }

    /// <summary>
    /// Downloads web pages.
    /// </summary>
    /// <param name="urls">Pages URLs.</param>
    /// <param name="destinationPath">Destination path.</param>
    /// <param name="parallelismDegree">Degree of parallelism.</param>
    /// <param name="timeout">Timeout for one page (headers and body).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result for each URL.</returns>
    public async Task<IReadOnlyList<DownloadResult>> DownloadAsync(Uri[] urls, string destinationPath, int parallelismDegree, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // valid urls
        var urlList = urls.ToLookup(IsValidUrl);
        var validUrls = urlList[true].ToList();
        foreach (var invalidUrl in urlList[false])
        {
            this.logger.LogWarning("Skipping invalid URL ({Url})", invalidUrl);
        }

        // unique urls
        var groups = validUrls.GroupBy(UriExtensions.GetRequestKey).ToArray();
        var uniqueUrls = groups.Select(g => g.First()).ToArray();

        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            this.logger.LogWarning("URL {Url} is specified {Count} times, downloading once", group.First(), group.Count());
        }

        var invalidResults = urlList[false].Select(url => new DownloadResult(url, DownloadStatus.InvalidUrl, Error: "Not a valid URL")).ToArray();
        var results = new DownloadResult[uniqueUrls.Length];

        this.fileService.CreateDirectory(destinationPath);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, uniqueUrls.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelismDegree,
                CancellationToken = cancellationToken,
            },
            async (index, internalCancellationToken) =>
            {
                var url = uniqueUrls[index];
                results[index] = await this.DownloadInternalAsync(url, destinationPath, timeout, internalCancellationToken);
            });

        return [.. invalidResults, .. results];
    }

    private static bool IsValidUrl(Uri? url)
    {
        return url is { IsAbsoluteUri: true }
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
    }

    private async Task<DownloadResult> DownloadInternalAsync(Uri url, string destinationPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var token = timeoutSource.Token;

            using var response = await this.httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var path = Path.Combine(destinationPath, url.ToFileName());
            await using var content = await response.Content.ReadAsStreamAsync(token);
            await this.fileService.WriteAsync(path, content, token);

            this.logger.LogInformation("Downloaded {Url} to ({Path})", url, path);

            return new DownloadResult(url, DownloadStatus.Succeeded, path);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            this.logger.LogWarning("Download timeout {Url}", url);

            return new DownloadResult(url, DownloadStatus.TimedOut, null, "Request timed out");
        }
        catch (HttpRequestException ex)
        {
            this.logger.LogWarning("Error downloading {Url}: {Reason}", url, ex.Message);

            return new DownloadResult(url, DownloadStatus.Failed, null, ex.Message);
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error downloading {Url}", url);

            return new DownloadResult(url, DownloadStatus.Failed, null, ex.Message);
        }
    }
}
