namespace PagesDownloader.Tests.Fakes;

/// <summary>
/// Fake HttpMessageHandler implementation.
/// </summary>
internal class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;
    private int requestCount;

    /// <summary>
    /// ctor.
    /// </summary>
    /// <param name="handler">Handler.</param>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        this.handler = handler;
    }

    /// <summary>
    /// Request count.
    /// </summary>
    public int RequestCount => this.requestCount;

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.requestCount);
        return this.handler(request, cancellationToken);
    }
}
