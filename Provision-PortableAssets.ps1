[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repositoryRoot = $PSScriptRoot
$temporaryDirectory = Join-Path $env:TEMP ('v2rayn-portable-assets-' + [guid]::NewGuid().ToString('N'))
$bundleCommit = 'b83a174e95996e19826edc9e0498323e40bf7a63'
$bundleGitBlobSha1 = '954fc3ea6f3a4afe4d935722afe6ce88665b19f9'
$dbIpRelease = '2026-11'
$dbIpMmdbSha1 = '09eea8433fe3eed71984fd6aac9d8ab3d9ca51f0'

function Copy-VerifiedBundleDirectory {
    param(
        [Parameter(Mandatory)]
        [IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)]
        [string]$ExecutableName,
        [Parameter(Mandatory)]
        [string]$DestinationName
    )

    $matches = @($Archive.Entries | Where-Object {
        $_.FullName -match ('(?i)(?:^|/)bin/' + [regex]::Escape($DestinationName) + '/' + [regex]::Escape($ExecutableName) + '$')
    })
    if ($matches.Count -ne 1) {
        throw "The pinned core bundle must contain exactly one bin/$DestinationName/$ExecutableName."
    }

    $prefix = $matches[0].FullName.Substring(0, $matches[0].FullName.LastIndexOf('/') + 1)
    $stagingDirectory = Join-Path $temporaryDirectory $DestinationName
    New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
    foreach ($entry in $Archive.Entries) {
        if (-not $entry.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or $entry.FullName.EndsWith('/')) {
            continue
        }

        $relativePath = $entry.FullName.Substring($prefix.Length).Replace('/', '\')
        $destinationPath = Join-Path $stagingDirectory $relativePath
        $fullDestinationPath = [IO.Path]::GetFullPath($destinationPath)
        if (-not $fullDestinationPath.StartsWith(
                [IO.Path]::GetFullPath($stagingDirectory) + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe path in pinned core bundle: $($entry.FullName)"
        }

        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationPath) | Out-Null
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Create($destinationPath)
        try {
            $inputStream.CopyTo($outputStream)
        }
        finally {
            $outputStream.Dispose()
            $inputStream.Dispose()
        }
    }

    $stagedExecutable = Join-Path $stagingDirectory $ExecutableName
    if (-not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf)) {
        throw "The core bundle did not stage $ExecutableName."
    }

    $destinationDirectory = Join-Path $repositoryRoot "bin\$DestinationName"
    if (Test-Path -LiteralPath $destinationDirectory) {
        $existingDirectory = Get-Item -LiteralPath $destinationDirectory -Force
        if (($existingDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to replace a linked runtime directory: $destinationDirectory"
        }
        Remove-Item -LiteralPath $destinationDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationDirectory) | Out-Null
    Move-Item -LiteralPath $stagingDirectory -Destination $destinationDirectory
}

$gitRoot = (& git -C $repositoryRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot.Trim()) -ne [IO.Path]::GetFullPath($repositoryRoot)) {
    throw 'Run this script from the repository root.'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $bundlePath = Join-Path $temporaryDirectory 'v2rayN-windows-64.zip'
    $bundleUrl = "https://raw.githubusercontent.com/2dust/v2rayN-core-bin/$bundleCommit/v2rayN-windows-64.zip"
    Invoke-WebRequest -Uri $bundleUrl -OutFile $bundlePath
    $actualBundleHash = (& git -C $repositoryRoot hash-object -t blob $bundlePath).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualBundleHash -ne $bundleGitBlobSha1) {
        throw "The pinned v2rayN core bundle failed its Git blob hash check: $actualBundleHash"
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($bundlePath)
    try {
        Copy-VerifiedBundleDirectory -Archive $archive -ExecutableName 'xray.exe' -DestinationName 'xray'
        Copy-VerifiedBundleDirectory -Archive $archive -ExecutableName 'sing-box.exe' -DestinationName 'sing_box'
    }
    finally {
        $archive.Dispose()
    }

    $databaseArchive = Join-Path $temporaryDirectory "dbip-country-lite-$dbIpRelease.mmdb.gz"
    $databaseTemp = Join-Path $temporaryDirectory 'GeoLite2-Country.mmdb'
    $databaseUrl = "https://download.db-ip.com/free/dbip-country-lite-$dbIpRelease.mmdb.gz"
    Invoke-WebRequest -Uri $databaseUrl -OutFile $databaseArchive

    $compressedStream = [IO.File]::OpenRead($databaseArchive)
    $decompressedStream = [IO.Compression.GZipStream]::new(
        $compressedStream,
        [IO.Compression.CompressionMode]::Decompress)
    $databaseStream = [IO.File]::Create($databaseTemp)
    try {
        $decompressedStream.CopyTo($databaseStream)
    }
    finally {
        $databaseStream.Dispose()
        $decompressedStream.Dispose()
        $compressedStream.Dispose()
    }

    $actualDatabaseHash = (Get-FileHash -LiteralPath $databaseTemp -Algorithm SHA1).Hash.ToLowerInvariant()
    if ($actualDatabaseHash -ne $dbIpMmdbSha1) {
        throw "The DB-IP Country Lite database failed its published SHA-1 check: $actualDatabaseHash"
    }
    if ((Get-Item -LiteralPath $databaseTemp).Length -lt 1000000) {
        throw 'The downloaded DB-IP Country Lite database is unexpectedly small.'
    }

    $databaseDirectory = Join-Path $repositoryRoot 'dat'
    New-Item -ItemType Directory -Force -Path $databaseDirectory | Out-Null
    Move-Item -LiteralPath $databaseTemp -Destination (Join-Path $databaseDirectory 'GeoLite2-Country.mmdb') -Force

    $assetMetadata = [ordered]@{
        coreBundle = [ordered]@{
            repository = '2dust/v2rayN-core-bin'
            commit = $bundleCommit
            gitBlobSha1 = $bundleGitBlobSha1
            executables = [ordered]@{
                xray = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'bin\Xray\xray.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
                singBox = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'bin\sing_box\sing-box.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        geoIp = [ordered]@{
            provider = 'DB-IP'
            product = 'IP to Country Lite'
            edition = $dbIpRelease
            source = $databaseUrl
            license = 'CC BY 4.0'
            licenseUrl = 'https://creativecommons.org/licenses/by/4.0/'
            publisherSha1 = $dbIpMmdbSha1
            sha256 = (Get-FileHash -LiteralPath (Join-Path $databaseDirectory 'GeoLite2-Country.mmdb') -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $assetMetadataPath = Join-Path $databaseDirectory 'portable-assets.json'
    $assetMetadata | ConvertTo-Json -Depth 8 |
        Set-Content -LiteralPath $assetMetadataPath -Encoding utf8

    Write-Output "Pinned core bundle Git blob SHA-1: $actualBundleHash"
    Write-Output "DB-IP Country Lite $dbIpRelease published SHA-1: $actualDatabaseHash"
    Write-Output "Xray SHA-256: $((Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'bin\Xray\xray.exe') -Algorithm SHA256).Hash)"
    Write-Output "sing-box SHA-256: $((Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'bin\sing_box\sing-box.exe') -Algorithm SHA256).Hash)"
    Write-Output "DB-IP Country Lite SHA-256: $((Get-FileHash -LiteralPath (Join-Path $databaseDirectory 'GeoLite2-Country.mmdb') -Algorithm SHA256).Hash)"
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
