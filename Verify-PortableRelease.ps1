[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath,

    [Parameter(Mandatory)]
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
foreach ($path in @($ArchivePath, $ManifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release file does not exist: $path"
    }
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw 'Unsupported release manifest schema.'
}
if ($manifest.source.commit -notmatch '^[0-9a-f]{40}$') {
    throw 'The release manifest must contain a full Git commit SHA.'
}
if ($manifest.archive.file -ne [IO.Path]::GetFileName($ArchivePath)) {
    throw 'The manifest archive filename does not match the archive being verified.'
}

$archiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.archive.sha256 -notmatch '^[0-9a-f]{64}$' -or $archiveHash -ne $manifest.archive.sha256) {
    throw 'The portable archive SHA-256 does not match the release manifest.'
}
if (-not $manifest.coreBundle -or -not $manifest.geoIp) {
    throw 'A distributable release manifest must identify the pinned core bundle and Geo-IP database.'
}
if ($manifest.coreBundle.gitBlobSha1 -notmatch '^[0-9a-f]{40}$' -or
    $manifest.coreBundle.executables.xray -notmatch '^[0-9a-f]{64}$' -or
    $manifest.coreBundle.executables.singBox -notmatch '^[0-9a-f]{64}$') {
    throw 'The core bundle metadata is incomplete or malformed.'
}
if ($manifest.geoIp.license -ne 'CC BY 4.0' -or
    $manifest.geoIp.publisherSha1 -notmatch '^[0-9a-f]{40}$' -or
    $manifest.geoIp.sha256 -notmatch '^[0-9a-f]{64}$') {
    throw 'The Geo-IP database metadata is incomplete or malformed.'
}

Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    $expectedEntries = [ordered]@{
        'bin/Xray/xray.exe' = $manifest.coreBundle.executables.xray
        'bin/sing_box/sing-box.exe' = $manifest.coreBundle.executables.singBox
        'dat/GeoLite2-Country.mmdb' = $manifest.geoIp.sha256
    }
    foreach ($entryName in $expectedEntries.Keys) {
        $entries = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq $entryName })
        if ($entries.Count -ne 1) {
            throw "The portable archive must contain exactly one '$entryName' entry."
        }

        $input = $entries[0].Open()
        $hash = [Security.Cryptography.SHA256]::Create()
        try {
            $actualEntryHash = [BitConverter]::ToString($hash.ComputeHash($input)).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $hash.Dispose()
            $input.Dispose()
        }
        if ($actualEntryHash -ne $expectedEntries[$entryName]) {
            throw "The '$entryName' entry does not match its release manifest SHA-256."
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Output "Portable release verified: $ArchivePath"
