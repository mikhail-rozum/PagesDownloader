namespace PagesDownloader.Tests;

using System.Text.RegularExpressions;
using PagesDownloader.Library;

/// <summary>
/// Contains tests for <see cref="UriExtensions"/>
/// </summary>
public class UriExtensionsTests
{
    /// <summary>
    /// File name should consist of readable part, 8 char hash and extension.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/", "example.com_index_", ".html")]
    [InlineData("https://example.com/docs", "example.com_docs_", ".html")]
    [InlineData("https://example.com/a/b/", "example.com_a_b_index_", ".html")]
    [InlineData("https://example.com/page.php", "example.com_page.php_", ".html")]
    [InlineData("https://example.com/index.html", "example.com_index_", ".html")]
    [InlineData("https://example.com/page.HTM", "example.com_page_", ".HTM")]
    [InlineData("https://en.wikipedia.org/wiki/.NET", "en.wikipedia.org_wiki_.NET_", ".html")]
    [InlineData("https://en.wikipedia.org/wiki/Node.js", "en.wikipedia.org_wiki_Node.js_", ".html")]
    [InlineData("https://example.com/v1.2/docs", "example.com_v1.2_docs_", ".html")]
    public void ToFileName_VariousPaths_ReturnsReadablePartHashAndExtension(string url, string expectedPrefix, string expectedExtension)
    {
        var fileName = new Uri(url).ToFileName();

        var pattern = $"^{Regex.Escape(expectedPrefix)}[0-9a-f]{{8}}{Regex.Escape(expectedExtension)}$";
        Assert.Matches(pattern, fileName);
    }

    /// <summary>
    /// Different URLs should have different file names.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/search?q=a", "https://example.com/search?q=b")]
    [InlineData("https://example.com/page", "http://example.com/page")]
    [InlineData("https://example.com/a/b", "https://example.com/a_b")]
    public void ToFileName_DifferentUrls_ReturnsDifferentNames(string first, string second)
    {
        Assert.NotEqual(new Uri(first).ToFileName(), new Uri(second).ToFileName());
    }

    /// <summary>
    /// Long URL should produce a valid file name.
    /// </summary>
    [Fact]
    public void ToFileName_VeryLongPath_ReturnsValidFileName()
    {
        var url = new Uri("https://example.com/" + new string('a', 500) + "?q=" + new string('b', 500));

        var fileName = url.ToFileName();

        Assert.True(fileName.Length <= 255);
        Assert.Equal(-1, fileName.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    /// <summary>
    /// Same URLs should have the same key.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/page", "https://EXAMPLE.com/page")]
    [InlineData("https://example.com/page", "https://example.com:443/page")]
    [InlineData("https://example.com/page", "https://example.com/page#top")]
    public void GetRequestKey_EquivalentUrls_ReturnsSameKey(string first, string second)
    {
        Assert.Equal(new Uri(first).GetRequestKey(), new Uri(second).GetRequestKey());
    }

    /// <summary>
    /// Different URLs should have different keys.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/page", "http://example.com/page")]
    [InlineData("https://example.com/page", "https://example.com/page/")]
    [InlineData("https://example.com/page", "https://example.com/Page")]
    [InlineData("https://example.com/page?a=1", "https://example.com/page?a=2")]
    public void GetRequestKey_DifferentUrls_ReturnsDifferentKeys(string first, string second)
    {
        Assert.NotEqual(new Uri(first).GetRequestKey(), new Uri(second).GetRequestKey());
    }
}
