# v2rayN

### A GUI client for Windows, Linux and macOS. Support [Xray](https://github.com/XTLS/Xray-core) and [sing-box](https://github.com/SagerNet/sing-box) and [others](https://github.com/2dust/v2rayN/wiki/List-of-supported-cores)

[![CodeFactor](https://www.codefactor.io/repository/github/2dust/v2rayn/badge)](https://www.codefactor.io/repository/github/2dust/v2rayn)
[![Release](https://img.shields.io/github/v/release/2dust/v2rayN?logo=github&label=Release)](https://github.com/2dust/v2rayN/releases)
[![Downloads](https://img.shields.io/github/downloads/2dust/v2rayN/latest/total?logo=github&label=Downloads)](https://github.com/2dust/v2rayN/releases)
[![Telegram](https://img.shields.io/badge/Telegram-Chat-26A5E4?logo=telegram)](https://t.me/v2rayn)
 
[![Windows](https://img.shields.io/badge/Windows-supported-0078D6?logo=windows)](https://github.com/2dust/v2rayN) 
[![Linux](https://img.shields.io/badge/Linux-supported-FCC624?logo=linux&logoColor=000)](https://github.com/2dust/v2rayN) 
[![macOS](https://img.shields.io/badge/macOS-supported-000000?logo=apple)](https://github.com/2dust/v2rayN) 
[![GPG Signed](https://img.shields.io/badge/GPG-signed-4B32C3?logo=gnuprivacyguard)](https://github.com/2dust/v2rayN)


---

## Download / 下载

Download the latest release here:

在这里下载最新版本：

[https://github.com/2dust/v2rayN/releases](https://github.com/2dust/v2rayN/releases)


> [!TIP]
> v2rayN is the desktop version. For the mobile version, please visit the v2rayNG \
> v2rayN 是电脑版，手机版请访问 v2rayNG
>
> https://github.com/2dust/v2rayNG

---

## Documentation / 使用文档

Read the Wiki for usage guides and configuration details.

请阅读 Wiki 获取使用说明和配置教程。

[https://github.com/2dust/v2rayN/wiki](https://github.com/2dust/v2rayN/wiki)

---

## Supported Platforms / 支持平台

| Platform / 平台 | x64 | x86 | arm64 | riscv64 | loong64 |
| --- | --- | --- | --- | --- | --- |
| Windows | ✅ | ✅ | ✅ | - | - |
| Linux | ✅ | - | ✅ | ✅ | ✅ |
| macOS | ✅ | - | ✅ | - | - |

Minimum OS requirements: [Release files introduction](https://github.com/2dust/v2rayN/wiki/Release-files-introduction) / 最低系统要求：[发布文件介绍](https://github.com/2dust/v2rayN/wiki/Release-files-introduction)

---

## GPG Verification / GPG 签名校验

Release files are signed with GPG to verify authenticity and integrity, helping prevent mirror, ISP, or CDN hijacking.

发布文件已使用 GPG 签名，可用于校验文件真实性与完整性，预防镜像站、运营商或 CDN 劫持。

### Fingerprint / 公钥指纹

```text
7694 5E9F 3E9A 168F 8070 F195 805D 661C
134D FAF6 8903 C199 463C 31E5 AE90 3AE0
```

---

## Community / 社区

Telegram Group / Telegram 群组：

[https://t.me/v2rayN](https://t.me/v2rayN)

Telegram Channel / Telegram 频道：

[https://t.me/github_2dust](https://t.me/github_2dust)

---

## Portable extension modules

The Windows client includes a separate `v2rayN/CustomExtensions` project. It targets `net8.0-windows` and the upstream client's `net10.0-windows10.0.19041.0` framework; the official upstream source, protocol models, parsers, and UI XAML remain intact. The application startup has a small additive bootstrap that stores the Free Config Pack subscription through v2rayN's existing SQLite table and schedules its initial update in the background. Existing tray/close-to-tray behavior is retained.

The storage abstraction provides a relative-path JSON provider for legacy `guiNConfig.json` layouts and a callback-backed SQLite provider. The current .NET 10 client maps SQLite operations to its existing `SubItem` and `SQLiteHelper` APIs; no second database or parallel subscription schema is introduced. `GeoIpService` reads a compatible country MMDB from `dat/GeoLite2-Country.mmdb`, caches IP/domain lookups, and returns ISO country names and flag emoji. The supplied portable build uses DB-IP IP to Country Lite (November 2026), not MaxMind GeoLite2; it is included under that runtime-compatible filename and attributed in `PORTABLE-README.txt` under CC BY 4.0. IP lookup itself is offline; resolving a hostname to an IP uses DNS. The RTT helper limits HTTP probes to 100 concurrent requests and marks failures or delays above 5 seconds offline. Its default probe measures direct reachability to Google’s HTTP 204 endpoint; callers must supply a per-profile probe URI to measure through a particular node. On the .NET 10 client, `ConnectToLowestLatencyProfileAsync` uses the native `ProfilesViewModel.SetDefaultServer` path to select and reload the winning profile.

### Build and portable Windows package

Build the official solution with the .NET 10 SDK. The isolated extension project can also be built for .NET 8 Windows:

```powershell
dotnet build .\v2rayN\v2rayN.sln -c Release
dotnet build .\v2rayN\CustomExtensions\CustomExtensions.csproj -f net8.0-windows -c Release
dotnet run --project .\v2rayN\CustomExtensions.Tests\CustomExtensions.Tests.csproj -c Release
```

Provision the pinned Windows core bundle and DB-IP Country Lite database, then build the verified archive:

```powershell
.\Provision-PortableAssets.ps1
.\Build-Portable.ps1
tar -tf .\Output\v2rayN-Pro-Portable.zip
Get-FileHash .\Output\v2rayN-Pro-Portable.zip -Algorithm SHA256
```

`Provision-PortableAssets.ps1` verifies the core ZIP against its pinned Git blob ID and checks DB-IP's published SHA-1 before installing assets under the ignored `bin/` and `dat/` paths. The monthly `asset-refresh.yml` workflow verifies candidate upstream assets and opens a review-only PR for pin changes; it never auto-merges or publishes. `Build-Portable.ps1` runs the sidecar TUnit suites for .NET 8 and .NET 10, publishes the self-contained win-x64 WPF app, stages the app, sidecar, both core runtimes and country MMDB, then creates `Output/v2rayN-Pro-Portable.zip` and a machine-readable release manifest. `Verify-PortableRelease.ps1` checks the archive digest and the hashes of the packaged runtimes and database against that manifest. The build job has read-only repository permissions; the isolated tag-publish job alone receives release-write and OIDC permissions and produces GitHub build-provenance attestations for the ZIP, manifest, and checksum sidecar. These runtime binaries and the licensed database are not committed. The archive includes DB-IP IP to Country Lite attribution under CC BY 4.0. No repository variables or database license key are required. The existing `Publish-Portable.bat` remains available for preparing a publish directory; it does not create the ZIP archive.

The authenticated WebAPI endpoint `GET /api/diagnostics` returns a bounded ZIP containing allowlisted runtime metadata, capabilities, active operation categories, and recent sanitized logs. It does not serialize application settings, profile records, subscription records, or SQLite data; URL, credential, bearer-token, and absolute-path patterns are redacted. The failover policy engine is opt-in (disabled by default), persists its thresholds through `/api/settings/failover`, and exposes manual override controls. `ServiceLib.Services.ProfileFailoverCoordinator` accepts host-provided batched probe and profile-switch delegates and emits a route-switch event; the current clients do not yet provide an automatic probe/switch adapter, so enabling the setting alone does not initiate route changes.

The runtime uses upstream's existing portable data-directory selection and does not force elevation in the manifest. It may fall back to LocalAppData when the application directory is not writable; this extension does not override that upstream behavior. TUN setup may request administrator approval through the existing v2rayN mechanism; an unconditional `requireAdministrator` manifest would unnecessarily elevate every launch.

### Subscription-feed aggregation workflow

`.github/workflows/update-sub.yml` contains the six-hour aggregator intended for `rostamimohammadamin8-dot/Free-VPN-Config-Pack-2026`; it is guarded to run only in that repository. Copy the workflow into that repository's `.github/workflows/` directory and set its `VPN_SOURCE_URLS` Actions variable to newline-separated HTTPS feed URLs. It validates proxy URI schemes, deduplicates and Base64-encodes the nodes, and commits `sub.txt` only when the result changes.
