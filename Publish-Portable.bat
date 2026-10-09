@echo off
setlocal
cd /d "%~dp0"

set "PROJECT=v2rayN\v2rayN\v2rayN.csproj"
set "PUBLISH_DIR=publish"

dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

if not exist "bin\Xray" (
    echo Missing bin\Xray. Add the Xray Windows runtime before packaging.
    exit /b 1
)
if not exist "bin\Xray\xray.exe" (
    echo Missing bin\Xray\xray.exe.
    exit /b 1
)
if not exist "bin\sing_box" (
    echo Missing bin\sing_box. Add the sing-box Windows runtime before packaging.
    exit /b 1
)
if not exist "bin\sing_box\sing-box.exe" (
    echo Missing bin\sing_box\sing-box.exe.
    exit /b 1
)
if not exist "dat\GeoLite2-Country.mmdb" (
    echo Missing dat\GeoLite2-Country.mmdb. Obtain it under a valid MaxMind license before packaging.
    exit /b 1
)

if not exist "%PUBLISH_DIR%\bin" mkdir "%PUBLISH_DIR%\bin"
if not exist "%PUBLISH_DIR%\dat" mkdir "%PUBLISH_DIR%\dat"
xcopy "bin\Xray" "%PUBLISH_DIR%\bin\Xray\" /E /I /Y >nul
if errorlevel 1 exit /b 1
xcopy "bin\sing_box" "%PUBLISH_DIR%\bin\sing_box\" /E /I /Y >nul
if errorlevel 1 exit /b 1
copy /Y "dat\GeoLite2-Country.mmdb" "%PUBLISH_DIR%\dat\" >nul
if errorlevel 1 exit /b 1

echo Portable publish is ready in the publish directory.
exit /b 0
