[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$AllowMissingGeoIpForLocalTesting
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'Output'
$stageDirectory = Join-Path $outputDirectory '_stage'
$archivePath = Join-Path $outputDirectory 'v2rayN-Pro-Portable.zip'
$manifestPath = Join-Path $outputDirectory 'release-manifest.json'
$testProject = Join-Path $repositoryRoot 'v2rayN\CustomExtensions.Tests\CustomExtensions.Tests.csproj'
$hostProject = Join-Path $repositoryRoot 'v2rayN\v2rayN\v2rayN.csproj'
$sidecarProject = Join-Path $repositoryRoot 'v2rayN\CustomExtensions\CustomExtensions.csproj'
$geoIpSource = Join-Path $repositoryRoot 'dat\GeoLite2-Country.mmdb'
$xrayDirectory = Join-Path $repositoryRoot 'bin\Xray'
$singBoxDirectory = Join-Path $repositoryRoot 'bin\sing_box'
$sidecarOutput = Join-Path $repositoryRoot "v2rayN\CustomExtensions\bin\$Configuration\net10.0-windows10.0.19041.0"
$sidecarDll = Join-Path $sidecarOutput 'CustomExtensions.dll'
$sidecarManifest = Join-Path $sidecarOutput 'Packaging\sidecar-manifest.json'

function Assert-CoreSourcesUnchanged {
    $changedPaths = @(& git -C $repositoryRoot diff --name-only HEAD -- v2rayN)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not verify the tracked core-source diff with git.'
    }

    $untrackedPaths = @(& git -C $repositoryRoot ls-files --others --exclude-standard -- v2rayN)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not verify untracked core-source files with git.'
    }

    $disallowedPaths = @(
        $changedPaths + $untrackedPaths |
            Where-Object {
                $_ -and
                $_ -notlike 'v2rayN/CustomExtensions/*' -and
                $_ -notlike 'v2rayN/CustomExtensions.Tests/*' -and
                $_ -notlike 'v2rayN/v2rayN.WebAPI/*' -and
                $_ -notlike 'v2rayN/ServiceLib.Tests/*' -and
                $_ -ne 'v2rayN/ServiceLib/Services/ProfileFailoverCoordinator.cs' -and
                $_ -ne 'v2rayN/ServiceLib/Models/Configs/ProfileFailoverSettings.cs' -and
                $_ -ne 'v2rayN/ServiceLib/Models/Configs/Config.cs'
            } |
            Sort-Object -Unique
    )
    if ($disallowedPaths.Count -gt 0) {
        throw "Core or non-sidecar files differ from HEAD; refusing to package: $($disallowedPaths -join ', ')"
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SidecarTests {
    param(
        [Parameter(Mandatory)]
        [string]$TargetFramework,
        [Parameter(Mandatory)]
        [string]$RuntimeVersionPrefix
    )

    Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @(
        'build',
        $testProject,
        '-c', $Configuration,
        '-f', $TargetFramework
    )

    $testOutput = Join-Path $repositoryRoot "v2rayN\CustomExtensions.Tests\bin\$Configuration\$TargetFramework"
    $assemblyPath = Join-Path $testOutput 'CustomExtensions.Tests.dll'
    $depsPath = Join-Path $testOutput 'CustomExtensions.Tests.deps.json'
    $runtimeConfigPath = Join-Path $testOutput 'CustomExtensions.Tests.runtimeconfig.json'
    foreach ($path in @($assemblyPath, $depsPath, $runtimeConfigPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Test build did not produce the required runtime file: $path"
        }
    }

    $runtimeLines = @(& dotnet --list-runtimes)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enumerate installed .NET runtimes.'
    }

    $runtimeOptions = Get-Content -Raw -LiteralPath $runtimeConfigPath | ConvertFrom-Json
    $frameworks = @($runtimeOptions.runtimeOptions.frameworks)
    if ($frameworks.Count -eq 0) {
        $frameworks = @($runtimeOptions.runtimeOptions.framework)
    }

    $matchingVersions = @{}
    foreach ($framework in $frameworks) {
        $versions = @(
            foreach ($line in $runtimeLines) {
                if ($line -match ('^' + [regex]::Escape($framework.name) + '\s+(\d+\.\d+\.\d+)')) {
                    $version = [version]$Matches[1]
                    if ("$($version.Major).$($version.Minor)" -eq $RuntimeVersionPrefix) {
                        $version
                    }
                }
            }
        )
        if ($versions.Count -eq 0) {
            throw "No installed $($framework.name) runtime matching $RuntimeVersionPrefix was found."
        }
        $matchingVersions[$framework.name] = $versions
    }

    $commonVersions = @($matchingVersions[$frameworks[0].name])
    foreach ($framework in $frameworks | Select-Object -Skip 1) {
        $commonVersions = @($commonVersions | Where-Object { $_ -in $matchingVersions[$framework.name] })
    }
    if ($commonVersions.Count -eq 0) {
        throw "No common installed runtime patch is available for target $TargetFramework."
    }
    $selectedRuntimeVersion = ($commonVersions | Sort-Object -Descending | Select-Object -First 1).ToString()
    foreach ($framework in $frameworks) {
        $framework.version = $selectedRuntimeVersion
    }

    $temporaryRuntimeConfig = Join-Path $env:TEMP (
        'CustomExtensions-' + $TargetFramework + '-' + [guid]::NewGuid().ToString('N') + '.runtimeconfig.json')
    try {
        $runtimeOptions | ConvertTo-Json -Depth 20 |
            Set-Content -LiteralPath $temporaryRuntimeConfig -Encoding utf8
        Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @(
            'exec',
            '--runtimeconfig', $temporaryRuntimeConfig,
            '--depsfile', $depsPath,
            $assemblyPath
        )
    }
    finally {
        Remove-Item -LiteralPath $temporaryRuntimeConfig -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.git'))) {
    $gitRoot = (& git -C $repositoryRoot rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
        throw 'Run this script from a Git working tree.'
    }
}

Assert-CoreSourcesUnchanged

$requiredAssets = @(
    @{ Path = (Join-Path $xrayDirectory 'xray.exe'); Name = 'Xray Windows runtime' },
    @{ Path = (Join-Path $singBoxDirectory 'sing-box.exe'); Name = 'sing-box Windows runtime' }
)
if (-not $AllowMissingGeoIpForLocalTesting) {
    $requiredAssets += @{ Path = $geoIpSource; Name = 'licensed country MMDB' }
}

Invoke-SidecarTests -TargetFramework 'net8.0-windows' -RuntimeVersionPrefix '8.0'
Invoke-SidecarTests -TargetFramework 'net10.0-windows10.0.19041.0' -RuntimeVersionPrefix '10.0'
Assert-CoreSourcesUnchanged

foreach ($asset in $requiredAssets) {
    if (-not (Test-Path -LiteralPath $asset.Path -PathType Leaf)) {
        throw "Missing $($asset.Name): $($asset.Path). Provision the asset under its documented path, then rerun the script."
    }
}
if (Test-Path -LiteralPath $geoIpSource -PathType Leaf) {
    if ((Get-Item -LiteralPath $geoIpSource).Length -lt 1000000) {
    throw "Country MMDB is unexpectedly small: $geoIpSource"
    }
}
elseif ($AllowMissingGeoIpForLocalTesting) {
    Write-Warning 'LOCAL TESTING ONLY: the country MMDB is omitted. Do not distribute this archive; GeoIpService will not be available.'
}

if (Test-Path -LiteralPath $outputDirectory) {
    $resolvedOutput = [IO.Path]::GetFullPath($outputDirectory)
    $expectedOutput = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'Output'))
    if ($resolvedOutput -ne $expectedOutput -or (Split-Path -Leaf $resolvedOutput) -ne 'Output') {
        throw "Refusing to clean an unexpected output path: $resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @(
    'build',
    $sidecarProject,
    '-c', $Configuration,
    '-f', 'net10.0-windows10.0.19041.0'
)

Invoke-CheckedCommand -FilePath 'dotnet' -Arguments @(
    'publish',
    $hostProject,
    '-c', $Configuration,
    '-r', $RuntimeIdentifier,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-o', $stageDirectory
)

Assert-CoreSourcesUnchanged

$builtAssets = @(
    @{ Path = $sidecarDll; Name = 'built CustomExtensions.dll' },
    @{ Path = $sidecarManifest; Name = 'built sidecar-manifest.json' }
)
foreach ($asset in $builtAssets) {
    if (-not (Test-Path -LiteralPath $asset.Path -PathType Leaf)) {
        throw "Publish did not produce the required sidecar asset ($($asset.Name)): $($asset.Path)"
    }
}

$expectedExecutable = Join-Path $stageDirectory 'v2rayN.exe'
if (-not (Test-Path -LiteralPath $expectedExecutable -PathType Leaf)) {
    throw "Publish output did not contain the expected executable: $expectedExecutable"
}

Copy-Item -LiteralPath $sidecarDll -Destination (Join-Path $stageDirectory 'CustomExtensions.dll') -Force
Copy-Item -LiteralPath $sidecarManifest -Destination (Join-Path $stageDirectory 'sidecar-manifest.json') -Force
New-Item -ItemType Directory -Path (Join-Path $stageDirectory 'dat') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stageDirectory 'bin') -Force | Out-Null
if (Test-Path -LiteralPath $geoIpSource -PathType Leaf) {
    Copy-Item -LiteralPath $geoIpSource -Destination (Join-Path $stageDirectory 'dat\GeoLite2-Country.mmdb') -Force
}
$stageXrayDirectory = Join-Path $stageDirectory 'bin\Xray'
$stageSingBoxDirectory = Join-Path $stageDirectory 'bin\sing_box'
New-Item -ItemType Directory -Path $stageXrayDirectory, $stageSingBoxDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $xrayDirectory -Force | Copy-Item -Destination $stageXrayDirectory -Recurse -Force
Get-ChildItem -LiteralPath $singBoxDirectory -Force | Copy-Item -Destination $stageSingBoxDirectory -Recurse -Force

$portableReadme = @'
v2rayN-Pro Portable Edition

Extract this archive to a writable folder and run v2rayN.exe. The package does not install the application or write registry entries.

The existing v2rayN runtime stores its SQLite configuration under guiConfigs\guiNDB.db and runtime data beneath the application directory while that directory is writable. If the application directory is not writable, upstream v2rayN may fall back to LocalAppData; this package does not modify that host behavior. Use a writable extraction location for portable data locality. TUN operation may independently request administrator approval.

'@
if (Test-Path -LiteralPath (Join-Path $stageDirectory 'dat\GeoLite2-Country.mmdb') -PathType Leaf) {
    $databaseEdition = if ($assetMetadata) { $assetMetadata.geoIp.edition } else { 'unknown edition' }
    $portableReadme += @'

Country database attribution: IP Geolocation by DB-IP.com, IP to Country Lite, __DBIP_EDITION__.
Source: https://db-ip.com/db/download/ip-to-country-lite
License: Creative Commons Attribution 4.0 International (CC BY 4.0): https://creativecommons.org/licenses/by/4.0/
The MMDB is packaged under the runtime-compatible filename dat\GeoLite2-Country.mmdb; it is DB-IP data, not a MaxMind GeoLite2 database. The database is redistributed under CC BY 4.0.
'@
    $portableReadme = $portableReadme.Replace('__DBIP_EDITION__', $databaseEdition)
}
else {
    $portableReadme += @'

LOCAL TESTING ONLY: This archive omits the country MMDB. GeoIP lookup will not be available. Do not distribute this archive as a production build. Add a valid, licensed dat\GeoLite2-Country.mmdb and rebuild without -AllowMissingGeoIpForLocalTesting for production.
'@
}
Set-Content -LiteralPath (Join-Path $stageDirectory 'PORTABLE-README.txt') -Value $portableReadme -Encoding utf8

$requiredArchiveEntries = @(
    'v2rayN.exe',
    'CustomExtensions.dll',
    'sidecar-manifest.json',
    'bin/Xray/xray.exe',
    'bin/sing_box/sing-box.exe',
    'PORTABLE-README.txt'
)
if (Test-Path -LiteralPath (Join-Path $stageDirectory 'dat\GeoLite2-Country.mmdb') -PathType Leaf) {
    $requiredArchiveEntries += 'dat/GeoLite2-Country.mmdb'
}
$archiveEntries = Get-ChildItem -LiteralPath $stageDirectory -Force -Recurse -File |
    ForEach-Object { $_.FullName.Substring($stageDirectory.Length + 1).Replace('\', '/') }
$missingEntries = @($requiredArchiveEntries | Where-Object { $_ -notin $archiveEntries })
if ($missingEntries.Count -gt 0) {
    throw "Portable staging directory is missing required files: $($missingEntries -join ', ')"
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $stageDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $zippedEntries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $missingZippedEntries = @($requiredArchiveEntries | Where-Object { $_ -notin $zippedEntries })
    if ($missingZippedEntries.Count -gt 0) {
        throw "Created archive is missing required entries: $($missingZippedEntries -join ', ')"
    }
}
finally {
    $zip.Dispose()
}

$assetMetadataPath = Join-Path $repositoryRoot 'dat\portable-assets.json'
$assetMetadata = if (Test-Path -LiteralPath $assetMetadataPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $assetMetadataPath | ConvertFrom-Json
}
else {
    $null
}
$geoIpPath = Join-Path $stageDirectory 'dat\GeoLite2-Country.mmdb'
$geoIpMetadata = if ($assetMetadata -and (Test-Path -LiteralPath $geoIpPath -PathType Leaf)) {
    $assetMetadata.geoIp
}
else {
    $null
}
$manifest = [ordered]@{
    schemaVersion = 1
    source = [ordered]@{
        repository = 'rostamimohammadamin8-dot/v2rayN'
        commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        ref = if ($env:GITHUB_REF) { $env:GITHUB_REF } else { $null }
    }
    coreBundle = if ($assetMetadata) { $assetMetadata.coreBundle } else { $null }
    geoIp = if ($geoIpMetadata) {
        [ordered]@{
            provider = $geoIpMetadata.provider
            product = $geoIpMetadata.product
            edition = $geoIpMetadata.edition
            source = $geoIpMetadata.source
            license = $geoIpMetadata.license
            licenseUrl = $geoIpMetadata.licenseUrl
            publisherSha1 = $geoIpMetadata.publisherSha1
            sha256 = (Get-FileHash -LiteralPath $geoIpPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    else {
        $null
    }
    archive = [ordered]@{
        file = [IO.Path]::GetFileName($archivePath)
        sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest | ConvertTo-Json -Depth 12 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8

Assert-CoreSourcesUnchanged
Write-Output "Portable archive created and verified: $archivePath"
Write-Output "Release manifest created: $manifestPath"
