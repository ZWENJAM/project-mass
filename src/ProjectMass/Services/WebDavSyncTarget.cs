using System.Xml;
using System.Xml.Linq;

namespace ProjectMass.Services;

public sealed class WebDavSyncTarget(NasBrowser browser, Uri baseUri, string username, string password) : ISyncTarget
{
    private const string PropFindBody = """<?xml version="1.0" encoding="utf-8"?><propfind xmlns="DAV:"><prop><resourcetype/></prop></propfind>""";

    public static Uri ValidateUrl(string? input)
    {
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new SyncTargetException("Gib die WebDAV-Basis-URL ohne Zugangsdaten, Suchparameter oder Fragment ein.");
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
            throw new SyncTargetException("WebDAV braucht HTTPS. HTTP ist nur für localhost erlaubt (Mixed Content).");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + '/');
    }

    public async Task<string?> ReadAsync(string path)
    {
        var response = await RequestAsync("GET", PathUri(path));
        if (response.Status == 404) return null;
        Check(response.Status);
        return response.Content;
    }

    public async Task WriteAsync(string path, string content) =>
        Check((await PutAsync(path, content, new() { ["Content-Type"] = "application/json" })).Status);

    public async Task<bool> TryReplaceAsync(string path, string? expected, string content)
    {
        var current = await RequestAsync("GET", PathUri(path));
        if (current.Status != 404) Check(current.Status);
        if (!string.Equals(current.Content, expected, StringComparison.Ordinal)) return false;

        var headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
        if (current.Status == 404) headers["If-None-Match"] = "*";
        else if (current.Etag is { } tag && tag.StartsWith('"') && tag.EndsWith('"')) headers["If-Match"] = tag;
        var response = await PutAsync(path, content, headers);
        if (response.Status == 412) return false;
        Check(response.Status);
        return true;
    }

    public async Task<IReadOnlyList<string>> ListAsync(string folder)
    {
        var uri = PathUri(folder + '/');
        var response = await FindAsync(uri);
        if (response.Status == 404) return [];
        Check(response.Status);
        return ParseListing(response.Content ?? "", uri);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await RequestAsync("DELETE", PathUri(path));
        if (response.Status != 404) Check(response.Status);
    }

    public async Task TestAsync()
    {
        Check((await FindAsync(baseUri)).Status);
        await SyncTargetProbe.TestAsync(this);
    }

    public static IReadOnlyList<string> ParseListing(string xml, Uri folder)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
            });
            var document = XDocument.Load(reader);
            XNamespace dav = "DAV:";
            if (document.Root?.Name != dav + "multistatus")
                throw new XmlException();
            var prefix = folder.AbsolutePath.TrimEnd('/') + '/';
            // A reverse proxy can remove its public mount path before handing the request to WebDAV.
            var appOffset = prefix.LastIndexOf("/project-mass/", StringComparison.Ordinal);
            var appPrefix = appOffset >= 0 ? prefix[appOffset..] : null;
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in document.Root.Elements(dav + "response"))
            {
                var href = entry.Element(dav + "href")?.Value;
                var status = entry.Element(dav + "status")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
                var properties = entry.Elements(dav + "propstat").Where(prop =>
                    prop.Element(dav + "status")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) == "200");
                if ((status is not null && !status.StartsWith('2')) || string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(folder, href, out var file) ||
                    file.GetLeftPart(UriPartial.Authority) != folder.GetLeftPart(UriPartial.Authority) ||
                    properties.Any(prop => prop.Descendants(dav + "collection").Any())) continue;
                var path = file.AbsolutePath;
                var start = path.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : -1;
                if (start < 0 && appPrefix is not null && path.LastIndexOf(appPrefix, StringComparison.Ordinal) is >= 0 and var offset)
                    start = offset + appPrefix.Length;
                if (start < 0) continue;
                var name = Uri.UnescapeDataString(path[start..]);
                if (name.Length > 0 && name is not "." and not ".." && !name.Contains('/') && !name.Contains('\\')) files.Add(name);
            }
            return files.ToArray();
        }
        catch (XmlException) { throw new SyncTargetException("Der NAS hat kein gültiges WebDAV-Ordnerverzeichnis geliefert."); }
    }

    public static string ErrorMessage(int status) => status switch
    {
        401 => "WebDAV-Anmeldung fehlgeschlagen. Prüfe Benutzer und Passwort.",
        403 => "Der NAS-Benutzer hat keine ausreichenden Rechte für diesen Ordner.",
        404 => "Der WebDAV-Pfad wurde nicht gefunden. Prüfe die Basis-URL und den Ordner.",
        0 => "Der NAS ist nicht erreichbar oder erlaubt keine Anfragen von dieser App-Adresse. Gib CORS in den WebDAV-Einstellungen frei oder hoste die App auf dem NAS. Siehe README.",
        -1 => "Der NAS hat nicht rechtzeitig geantwortet. Prüfe die Verbindung.",
        _ => $"Der WebDAV-Server meldet HTTP {status}."
    };

    private Task<DavReply> FindAsync(Uri uri) => RequestAsync("PROPFIND", uri, PropFindBody,
        new() { ["Depth"] = "1", ["Content-Type"] = "application/xml; charset=utf-8" });

    private async Task<DavReply> PutAsync(string path, string content, Dictionary<string, string> headers)
    {
        var uri = PathUri(path);
        var reply = await RequestAsync("PUT", uri, content, headers);
        if (reply.Status is not (404 or 409)) return reply;
        // The selected base folder must already exist. Create only app-owned descendants.
        Check((await FindAsync(baseUri)).Status);
        var parts = path.Split('/');
        for (var count = 1; count < parts.Length; count++)
        {
            var folder = PathUri(string.Join('/', parts.Take(count)) + '/');
            var made = await RequestAsync("MKCOL", folder);
            if (made.Status != 405) Check(made.Status);
        }
        return await RequestAsync("PUT", uri, content, headers);
    }

    private Uri PathUri(string path)
    {
        var parts = path.TrimEnd('/').Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains('\\')))
            throw new SyncTargetException("Ungültiger Pfad zum NAS.");
        return new Uri(baseUri, string.Join('/', parts.Select(Uri.EscapeDataString)) + (path.EndsWith('/') ? "/" : ""));
    }
    private Task<DavReply> RequestAsync(string method, Uri uri, string? content = null, Dictionary<string, string>? headers = null) =>
        browser.CallAsync<DavReply>("webDavRequest", uri.AbsoluteUri, method, username, password, content, headers ?? []);

    private static void Check(int status)
    {
        if (status is < 200 or > 299) throw new SyncTargetException(ErrorMessage(status));
    }

    private sealed record DavReply(int Status, string? Content, string? Etag);
}
