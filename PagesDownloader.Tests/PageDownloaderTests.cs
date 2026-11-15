namespace PagesDownloader.Tests;

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PagesDownloader.Library;
using PagesDownloader.Tests.Fakes;

/// <summary>
/// Tests for <see cref="PageDownloader"/>.
/// </summary>
public class PageDownloaderTests
{
    private const string Destination = "out";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Succeed is saved through the file service and reported with Succeeded.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_SuccessfulResponse_SavesContentAndReturnsSucceeded()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(Html("<html>some</html>")));
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);

        var results = await downloader.DownloadAsync(
            [new Uri("https://example.com/page")], Destination, 2, TestTimeout, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(DownloadStatus.Succeeded, result.Status);
        Assert.NotNull(result.FilePath);
        Assert.Equal("<html>some</html>", fileService.Files[result.FilePath]);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// NotFound is reported as Failed and no file is written.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_NotFoundResponse_ReturnsFailedAndSavesNothing()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);

        var results = await downloader.DownloadAsync(
            [new Uri("https://example.com/missing")], Destination, 2, TestTimeout, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(DownloadStatus.Failed, result.Status);
        Assert.Null(result.FilePath);
        Assert.NotNull(result.Error);
        Assert.Empty(fileService.Files);
    }

    /// <summary>
    /// Failed URL should not affect other URLs.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_OneUrlFails_OtherUrlsStillDownloaded()
    {
        var handler = new FakeHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/broken"
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Html()));
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);
        Uri[] urls =
        [
            new("https://example.com/first"),
            new("https://example.com/broken"),
            new("https://example.com/second"),
        ];

        var results = await downloader.DownloadAsync(urls, Destination, 2, TestTimeout, CancellationToken.None);

        var expected = new[] { DownloadStatus.Succeeded, DownloadStatus.Failed, DownloadStatus.Succeeded };
        Assert.Equal(expected, results.Select(r => r.Status).ToArray());
        Assert.Equal(2, fileService.Files.Count);
    }

    /// <summary>
    /// Not HTTP(S) URLs should be marked as InvalidUrl.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_InvalidUrl_ReturnsInvalidUrlWithoutRequest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(Html()));
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);
        Uri[] urls =
        [
            new("ftp://example.com/file.txt"),
            new("relative/path", UriKind.Relative),
        ];

        var results = await downloader.DownloadAsync(urls, Destination, 2, TestTimeout, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(DownloadStatus.InvalidUrl, r.Status));
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(fileService.Files);
    }

    /// <summary>
    /// Same URLs should be served only once.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_DuplicateUrls_DownloadsOnce()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(Html()));
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);
        Uri[] urls =
        [
            new("https://example.com/page"),
            new("https://EXAMPLE.com/page"),
            new("https://example.com/page#top"),
        ];

        var results = await downloader.DownloadAsync(urls, Destination, 2, TestTimeout, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(1, handler.RequestCount);
    }

    /// <summary>
    /// URLs with timeout error should be marked as TimedOut.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_SlowResponse_ReturnsTimedOut()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Html();
        });
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);

        var results = await downloader.DownloadAsync(
            [new Uri("https://example.com/slow")], Destination, 2, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal(DownloadStatus.TimedOut, result.Status);
        Assert.Empty(fileService.Files);
    }

    /// <summary>
    /// Cancelling should cause OperationCanceledException.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Html();
        });
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);

        var downloadTask = downloader.DownloadAsync(
            [new Uri("https://example.com/page")], Destination, 2, TestTimeout, cancellation.Token);
        await requestStarted.Task;
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask);
        Assert.Empty(fileService.Files);
    }

    /// <summary>
    /// Parallelism (number of simultaneously run downloads) should be equal to specified parameter.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ManyUrls_DoesNotExceedParallelismDegree()
    {
        const int parallelism = 3;
        const int urlCount = 20;

        var sync = new object();
        var inFlight = 0;
        var maxInFlight = 0;
        var arrived = new SemaphoreSlim(0);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            lock (sync)
            {
                inFlight++;
                maxInFlight = Math.Max(maxInFlight, inFlight);
            }

            arrived.Release();
            await release.Task.WaitAsync(cancellationToken);

            lock (sync)
            {
                inFlight--;
            }

            return Html();
        });
        var downloader = CreateDownloader(handler, new FakeFileService());
        var urls = Enumerable.Range(0, urlCount).Select(i => new Uri($"https://example.com/page{i}")).ToArray();

        var downloadTask = downloader.DownloadAsync(urls, Destination, parallelism, TestTimeout, CancellationToken.None);

        // wait until 'parallelism' requests are started
        for (var i = 0; i < parallelism; i++)
        {
            Assert.True(await arrived.WaitAsync(TestTimeout), "Expected requests did not start");
        }

        // wait a bit: extra requests would start here if the limit was not enforced
        await Task.Delay(100);
        release.SetResult();
        var results = await downloadTask;

        Assert.Equal(urlCount, results.Count);
        Assert.All(results, r => Assert.Equal(DownloadStatus.Succeeded, r.Status));
        Assert.Equal(parallelism, maxInFlight);
    }

    /// <summary>
    /// Destination directory should be created.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_Always_CreatesDestinationDirectory()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(Html()));
        var fileService = new FakeFileService();
        var downloader = CreateDownloader(handler, fileService);

        await downloader.DownloadAsync(
            [new Uri("https://example.com/page")], Destination, 2, TestTimeout, CancellationToken.None);

        Assert.Contains(Destination, fileService.CreatedDirectories);
    }

    private static HttpResponseMessage Html(string body = "<html></html>")
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private static PageDownloader CreateDownloader(FakeHttpMessageHandler handler, FakeFileService fileService)
    {
        return new PageDownloader(new HttpClient(handler), fileService, NullLogger<PageDownloader>.Instance);
    }
}
