using ProjectMass.Services;

namespace ProjectMass.Tests;

public sealed class WebDavSyncTests
{
    [Theory]
    [InlineData("https://nas.example/webdav", "https://nas.example/webdav/")]
    [InlineData("http://localhost:5005/dav", "http://localhost:5005/dav/")]
    public void NormalizesBaseUrls(string input, string expected) =>
        Assert.Equal(expected, WebDavSyncTarget.ValidateUrl(input).AbsoluteUri);

    [Theory]
    [InlineData("/dav")]
    [InlineData("http://nas.example/webdav")]
    [InlineData("http://127.0.0.1:5005/dav")]
    [InlineData("https://alex:pw@nas.example/webdav")]
    [InlineData("https://nas.example/webdav?key=abc")]
    [InlineData("https://nas.example/webdav#path")]
    public void RejectsInsecureOrAmbiguousUrls(string input) =>
        Assert.Throws<SyncTargetException>(() => WebDavSyncTarget.ValidateUrl(input));

    [Theory]
    [InlineData(401, "Benutzer und Passwort")]
    [InlineData(404, "Pfad")]
    [InlineData(0, "CORS")]
    public void MapsUsefulErrors(int status, string message) =>
        Assert.Contains(message, WebDavSyncTarget.ErrorMessage(status));

    [Fact]
    public void ParsesOnlyDepthOneFilesFromDavMultiStatus()
    {
        const string listing = """
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response><d:href>/mount/project-mass/backups/</d:href></d:response>
              <d:response><d:href>/mount/project-mass/backups/2026-11-18.json</d:href></d:response>
              <d:response><d:href>https://nas.example/mount/project-mass/backups/konflikt%20PC.json</d:href></d:response>
              <d:response><d:href>/project-mass/backups/2026-11-17.json</d:href></d:response>
              <d:response><d:href>/mount/project-mass/backups/nested</d:href>
                <d:propstat><d:status>HTTP/1.1 200 OK</d:status><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop></d:propstat>
              </d:response>
              <d:response><d:href>/mount/project-mass/backups/nested/2026-11-16.json</d:href></d:response>
              <d:response><d:href>https://external.example/project-mass/backups/other.json</d:href></d:response>
            </d:multistatus>
            """;
        Assert.Equal(["2026-11-17.json", "2026-11-18.json", "konflikt PC.json"],
            WebDavSyncTarget.ParseListing(listing, new("https://nas.example/mount/project-mass/backups/")).Order());
    }

    [Fact]
    public void RejectsNonDavXmlAndDtd()
    {
        var folder = new Uri("https://nas.example/project-mass/backups/");
        Assert.Throws<SyncTargetException>(() => WebDavSyncTarget.ParseListing("<html />", folder));
        Assert.Throws<SyncTargetException>(() => WebDavSyncTarget.ParseListing(
            """<!DOCTYPE payload [<!ENTITY x "text">]><multistatus xmlns="DAV:">&x;</multistatus>""", folder));
    }
}
