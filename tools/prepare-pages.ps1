param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(?!\.{1,2}$)[A-Za-z0-9_.-]+$')]
    [string]$RepositoryName,

    [string]$WebRoot = 'publish/wwwroot'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $WebRoot).Path
$indexPath = Join-Path $root 'index.html'
$manifestPath = Join-Path $root 'service-worker-assets.js'
$index = [IO.File]::ReadAllText($indexPath)
$originalBase = '<base href="/" />'
if (!$index.Contains($originalBase)) {
    throw 'Die veröffentlichte index.html muss die lokale Basis "/" enthalten.'
}

$manifestScript = [IO.File]::ReadAllText($manifestPath)
$match = [regex]::Match($manifestScript, '\A\s*self\.assetsManifest\s*=\s*(?<json>\{.*\})\s*;?\s*\z', 'Singleline')
if (!$match.Success) {
    throw 'Das generierte Service-Worker-Manifest konnte nicht gelesen werden.'
}
$manifest = $match.Groups['json'].Value | ConvertFrom-Json
$indexAssets = @($manifest.assets | Where-Object url -CEQ 'index.html')
if ($indexAssets.Count -ne 1) {
    throw 'Die index.html fehlt im Service-Worker-Manifest oder ist mehrfach vorhanden.'
}

$index = $index.Replace($originalBase, ('<base href="/{0}/" />' -f $RepositoryName))
$indexBytes = [Text.Encoding]::UTF8.GetBytes($index)
$indexHash = [Security.Cryptography.SHA256]::HashData($indexBytes)
$indexAssets[0].hash = 'sha256-' + [Convert]::ToBase64String($indexHash)
$manifest.version = "$($manifest.version)-$(([Convert]::ToHexString($indexHash)).Substring(0, 16))"

[IO.File]::WriteAllBytes($indexPath, $indexBytes)
[IO.File]::WriteAllBytes((Join-Path $root '404.html'), $indexBytes)
[IO.File]::WriteAllText((Join-Path $root '.nojekyll'), '')
[IO.File]::WriteAllText($manifestPath, ('self.assetsManifest = ' + ($manifest | ConvertTo-Json -Depth 8 -Compress) + ';'))

# These precompressed files still contain the original base or manifest.
foreach ($file in @('index.html.br', 'index.html.gz', 'service-worker-assets.js.br', 'service-worker-assets.js.gz')) {
    $path = Join-Path $root $file
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path
    }
}
