using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoxAngelos.IntegrationTests.TestSupport;

/// <summary>
/// Drives the real running app over plain HTTP the same way a browser would,
/// including scraping the antiforgery token every Razor Pages POST requires.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Creates an HttpClient pointed at the locally running app, with its own cookie
    /// jar (so each test gets an isolated session) and no automatic redirect-following
    /// (so tests can assert on the 302 target directly).
    /// </summary>
    public static HttpClient CreateAppClient(bool allowAutoRedirect = false)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = allowAutoRedirect,
            // The app runs on the ASP.NET Core HTTPS dev certificate, which is
            // self-signed and not in the OS trust store used by this test runner.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(TestConfig.BaseUrl),
            Timeout = TimeSpan.FromSeconds(100)
        };
    }

    private static string AppendHandler(string pageUrl, string? handler)
    {
        if (handler == null) return pageUrl;
        var separator = pageUrl.Contains('?') ? '&' : '?';
        return $"{pageUrl}{separator}handler={handler}";
    }

    private static readonly Regex TokenRegex = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    /// <summary>GETs a page and scrapes its antiforgery form-field token.</summary>
    public static async Task<string> GetAntiforgeryTokenAsync(this HttpClient client, string pageUrl)
    {
        var response = await client.GetAsync(pageUrl);
        var html = await response.Content.ReadAsStringAsync();
        var match = TokenRegex.Match(html);
        if (!match.Success)
            throw new InvalidOperationException($"No antiforgery token found on {pageUrl} (status {response.StatusCode}).");
        return match.Groups[1].Value;
    }

    /// <summary>
    /// Posts an x-www-form-urlencoded body to a Razor Pages handler, including the
    /// antiforgery token scraped from <paramref name="pageUrl"/>.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client,
        string pageUrl,
        string? handler,
        Dictionary<string, string> fields)
    {
        var token = await client.GetAntiforgeryTokenAsync(pageUrl);
        var allFields = new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = token
        };

        var targetUrl = AppendHandler(pageUrl, handler);
        return await client.PostAsync(targetUrl, new FormUrlEncodedContent(allFields));
    }

    /// <summary>
    /// Posts a multipart/form-data body (file uploads) to a Razor Pages handler,
    /// including the antiforgery token scraped from <paramref name="pageUrl"/>.
    /// </summary>
    public static async Task<HttpResponseMessage> PostMultipartAsync(
        this HttpClient client,
        string pageUrl,
        string? handler,
        Dictionary<string, string> fields,
        IEnumerable<(string FieldName, string FileName, byte[] Bytes, string ContentType)>? files = null)
    {
        var token = await client.GetAntiforgeryTokenAsync(pageUrl);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        foreach (var (key, value) in fields)
            content.Add(new StringContent(value), key);

        if (files != null)
        {
            foreach (var (fieldName, fileName, bytes, contentType) in files)
            {
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                content.Add(fileContent, fieldName, fileName);
            }
        }

        var targetUrl = AppendHandler(pageUrl, handler);
        return await client.PostAsync(targetUrl, content);
    }

    /// <summary>
    /// Posts a JSON body to an AJAX-style Razor Pages handler, mirroring the app's own
    /// client-side fetch() calls: antiforgery token in the "RequestVerificationToken"
    /// header (see Program.cs AddAntiforgery HeaderName) plus X-Requested-With.
    /// </summary>
    public static async Task<HttpResponseMessage> PostJsonAsync(
        this HttpClient client,
        string pageUrl,
        string handler,
        object body)
    {
        var token = await client.GetAntiforgeryTokenAsync(pageUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, AppendHandler(pageUrl, handler))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("RequestVerificationToken", token);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await client.SendAsync(request);
    }
}
