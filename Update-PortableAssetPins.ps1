[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$temporaryDirectory = Join-Path $env:TEMP ('v2rayn-portable-refresh-' + [guid]::NewGuid().ToString('N'))
$provisionScript = Join-Path $PSScriptRoot 'Provision-PortableAssets.ps1'

function Set-Pin {
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Replacement
    )
    $regex = [regex]::new($Pattern)
    if ($regex.Matches($Content).Count -ne 1) {
        throw "Expected exactly one pinned '$Name' value in Provision-PortableAssets.ps1."
    }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $Replacement
    }.GetNewClosure()
    return $regex.Replace($Content, $evaluator, 1)
}

$gitRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot.Trim()) -ne [IO.Path]::GetFullPath($PSScriptRoot)) {
    throw 'Run this script from the repository root.'
}

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $latestCommit = Invoke-RestMethod -Uri 'https://api.github.com/repos/2dust/v2rayN-core-bin/commits?per_page=1' -Headers @{
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    if ($latestCommit.Count -lt 1 -or $latestCommit[0].sha -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve a full commit SHA for the upstream core bundle.'
    }
    $coreCommit = $latestCommit[0].sha

    $coreArchivePath = Join-Path $temporaryDirectory 'v2rayN-windows-64.zip'
    $coreArchiveUrl = "https://raw.githubusercontent.com/2dust/v2rayN-core-bin/$coreCommit/v2rayN-windows-64.zip"
    Invoke-WebRequest -Uri $coreArchiveUrl -OutFile $coreArchivePath
    $coreBlobSha1 = (& git -C $PSScriptRoot hash-object -t blob $coreArchivePath).Trim()
    if ($LASTEXITCODE -ne 0 -or $coreBlobSha1 -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not calculate the core bundle Git blob SHA-1.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $coreArchive = [IO.Compression.ZipFile]::OpenRead($coreArchivePath)
    try {
        foreach ($asset in @('xray.exe', 'sing-box.exe')) {
            $matches = @($coreArchive.Entries | Where-Object {
                $_.FullName -match ('(?i)(?:^|/)bin/(?:xray|sing_box)/' + [regex]::Escape($asset) + '$')
            })
            if ($matches.Count -ne 1) {
                throw "The candidate core bundle must contain exactly one $asset runtime."
            }
        }
    }
    finally {
        $coreArchive.Dispose()
    }

    $databasePage = (Invoke-WebRequest -Uri 'https://db-ip.com/db/download/ip-to-country-lite').Content
    $databaseText = [Net.WebUtility]::HtmlDecode([regex]::Replace($databasePage, '<[^>]+>', ' '))
    $databaseText = [regex]::Replace($databaseText, '\s+', ' ')
    $databaseMatch = [regex]::Match(
        $databaseText,
        'Format\s+MMDB\s+Release\s+([A-Za-z]+\s+\d{4}).*?SHA1SUM\s+([a-f0-9]{40})',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $databaseMatch.Success) {
        throw 'Could not read the current DB-IP Country Lite MMDB edition and publisher checksum.'
    }
    $edition = [datetime]::ParseExact($databaseMatch.Groups[1].Value, 'MMMM yyyy', [Globalization.CultureInfo]::InvariantCulture).ToString('yyyy-MM')
    $databaseSha1 = $databaseMatch.Groups[2].Value.ToLowerInvariant()

    $databaseArchivePath = Join-Path $temporaryDirectory "dbip-country-lite-$edition.mmdb.gz"
    $databaseUrl = "https://download.db-ip.com/free/dbip-country-lite-$edition.mmdb.gz"
    Invoke-WebRequest -Uri $databaseUrl -OutFile $databaseArchivePath
    $compressed = [IO.File]::OpenRead($databaseArchivePath)
    $decompressed = [IO.Compression.GZipStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
    $databasePath = Join-Path $temporaryDirectory 'GeoLite2-Country.mmdb'
    $database = [IO.File]::Create($databasePath)
    try {
        $decompressed.CopyTo($database)
    }
    finally {
        $database.Dispose()
        $decompressed.Dispose()
        $compressed.Dispose()
    }
    $actualDatabaseSha1 = (Get-FileHash -LiteralPath $databasePath -Algorithm SHA1).Hash.ToLowerInvariant()
    if ($actualDatabaseSha1 -ne $databaseSha1) {
        throw 'The downloaded DB-IP database does not match the publisher checksum.'
    }

    $content = Get-Content -Raw -LiteralPath $provisionScript
    $content = Set-Pin -Content $content -Name 'core commit' -Pattern "\`$bundleCommit = '[0-9a-f]{40}'" -Replacement "\`$bundleCommit = '$coreCommit'"
    $content = Set-Pin -Content $content -Name 'core bundle Git blob SHA-1' -Pattern "\`$bundleGitBlobSha1 = '[0-9a-f]{40}'" -Replacement "\`$bundleGitBlobSha1 = '$coreBlobSha1'"
    $content = Set-Pin -Content $content -Name 'DB-IP edition' -Pattern "\`$dbIpRelease = '[0-9]{4}-[0-9]{2}'" -Replacement "\`$dbIpRelease = '$edition'"
    $content = Set-Pin -Content $content -Name 'DB-IP publisher SHA-1' -Pattern "\`$dbIpMmdbSha1 = '[0-9a-f]{40}'" -Replacement "\`$dbIpMmdbSha1 = '$databaseSha1'"
    Set-Content -LiteralPath $provisionScript -Value $content -NoNewline -Encoding utf8

    Write-Output "Updated portable asset pins: core=$coreCommit DB-IP=$edition"
}
finally {
    Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
