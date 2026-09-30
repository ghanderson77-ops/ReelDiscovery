using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ReelDiscovery.Tests.Fakes;

/// <summary>
/// Canned-response HTTP handler used to exercise the OpenAI and Anthropic SDKs without network access.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public int Status { get; set; } = 200;
    public string Body { get; set; } = "{}";
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// When set, returned as ByteArrayContent with <see cref="ContentType"/> instead of <see cref="Body"/>.
    /// </summary>
    public byte[]? BinaryBody { get; set; }

    /// <summary>
    /// When set, thrown from SendAsync to simulate a network failure.
    /// </summary>
    public Exception? ThrowOnSend { get; set; }

    /// <summary>
    /// When non-empty, one entry is dequeued per call instead of using Status/Body.
    /// </summary>
    public Queue<(int Status, string Body)> Responses { get; } = new();

    public string? LastRequestBody { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public List<string> RequestBodies { get; } = new();
    public int Calls { get; private set; }

    public HttpClient CreateClient() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        LastRequestUri = request.RequestUri;
        LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
        RequestBodies.Add(LastRequestBody ?? string.Empty);

        if (ThrowOnSend != null)
        {
            throw ThrowOnSend;
        }

        if (Responses.Count > 0)
        {
            var (status, body) = Responses.Dequeue();
            return new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, ContentType)
            };
        }

        HttpContent content;
        if (BinaryBody != null)
        {
            var bytes = new ByteArrayContent(BinaryBody);
            bytes.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
            content = bytes;
        }
        else
        {
            content = new StringContent(Body, Encoding.UTF8, ContentType);
        }

        return new HttpResponseMessage((HttpStatusCode)Status) { Content = content };
    }
}
