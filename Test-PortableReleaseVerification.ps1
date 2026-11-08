$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $env:TEMP ('portable-release-verification-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $testRoot 'stage'
$archivePath = Join-Path $testRoot 'v2rayN-Pro-Portable.zip'
$manifestPath = Join-Path $testRoot 'release-manifest.json'
$verifierPath = Join-Path $PSScriptRoot 'Verify-PortableRelease.ps1'

function Assert-Throws {
    param([scriptblock]$Action, [string]$FailureMessage)
    try {
        & $Action
    }
    catch {
        return
    }
    throw $FailureMessage
}

New-Item -ItemType Directory -Force -Path (Join-Path $stage 'bin\Xray'), (Join-Path $stage 'bin\sing_box'), (Join-Path $stage 'dat') | Out-Null
try {
    Set-Content -LiteralPath (Join-Path $stage 'bin\Xray\xray.exe') -Value 'xray test runtime' -NoNewline
    Set-Content -LiteralPath (Join-Path $stage 'bin\sing_box\sing-box.exe') -Value 'sing-box test runtime' -NoNewline
    Set-Content -LiteralPath (Join-Path $stage 'dat\GeoLite2-Country.mmdb') -Value 'database test data' -NoNewline
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archivePath

    $metadata = [ordered]@{
        schemaVersion = 1
        source = [ordered]@{ commit = '0123456789abcdef0123456789abcdef01234567' }
        coreBundle = [ordered]@{
            gitBlobSha1 = '0123456789abcdef0123456789abcdef01234567'
            executables = [ordered]@{
                xray = (Get-FileHash (Join-Path $stage 'bin\Xray\xray.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
                singBox = (Get-FileHash (Join-Path $stage 'bin\sing_box\sing-box.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        geoIp = [ordered]@{
            license = 'CC BY 4.0'
            publisherSha1 = '0123456789abcdef0123456789abcdef01234567'
            sha256 = (Get-FileHash (Join-Path $stage 'dat\GeoLite2-Country.mmdb') -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        archive = [ordered]@{
            file = [IO.Path]::GetFileName($archivePath)
            sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    & $verifierPath -ArchivePath $archivePath -ManifestPath $manifestPath

    $metadata.archive.sha256 = 'f' * 64
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Assert-Throws {
        & $verifierPath -ArchivePath $archivePath -ManifestPath $manifestPath
    } 'Tampered manifest passed verification.'

    $metadata.archive.sha256 = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    [IO.File]::AppendAllText($archivePath, 'tamper')
    Assert-Throws {
        & $verifierPath -ArchivePath $archivePath -ManifestPath $manifestPath
    } 'Tampered archive passed verification.'

    Write-Output 'Portable release verification tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
