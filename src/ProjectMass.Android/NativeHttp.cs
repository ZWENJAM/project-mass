using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ProjectMass.Services;

namespace ProjectMass.Mobile;

internal sealed class PackagedSeedHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;
        if (request.Method != HttpMethod.Get || path is not ("/data/exercises.json" or "/data/workout-plans.json" or "/data/snacks.json"))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        cancellationToken.ThrowIfCancellationRequested();
        var stream = await FileSystem.Current.OpenAppPackageFileAsync("wwwroot" + path);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
        };
    }
}

internal sealed class NativeWebDavTransport : IWebDavTransport, IDisposable
{
    public string NetworkFailureMessage => "Der NAS ist nicht erreichbar. Prüfe die Verbindung, die WebDAV-Adresse und das HTTPS-Zertifikat.";

    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<WebDavResponse> RequestAsync(Uri uri, string method, string username, string password,
        string? content, IReadOnlyDictionary<string, string> headers)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        if (content is not null) request.Content = new StringContent(content, Encoding.UTF8);
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && request.Content is not null)
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
            else
                request.Headers.TryAddWithoutValidation(name, value);
        }
        try
        {
            using var response = await client.SendAsync(request);
            var body = response.IsSuccessStatusCode && method is "GET" or "PROPFIND"
                ? await response.Content.ReadAsStringAsync() : null;
            return new((int)response.StatusCode, body, response.Headers.ETag?.ToString());
        }
        catch (OperationCanceledException) { return new(-1, null, null); }
        catch (HttpRequestException) { return new(0, null, null); }
    }

    public void Dispose() => client.Dispose();
}
