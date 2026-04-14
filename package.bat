@echo off
rem Collect release artifacts after building odm.sln (Release|x64).
rem
rem Prerequisites:
rem   1. odm\odm.player\odm.player.sln must be built first (Release|x64) so
rem      odm.player.net.dll is present — see .github/workflows/odm.yml.
rem   2. odm.ui.app must be built (Release|x64), which copies project references
rem      (odm.player.net.dll, odm.player.host.exe, odm.player.media.dll) into
rem      its output directory via CopyToOutputDirectory.

if not exist build mkdir build

rem --- managed app + all copied project references (incl. odm.player.net.dll) ---
copy /Y .\odm\odm.ui.app\bin\Release\x64\*.* build\

rem --- native player DLL: copy from its own output as an explicit fallback in
rem     case the project-reference copy was skipped (e.g. incremental build) ---
if exist .\odm\odm.player\odm.player.net\bin\x64\Release\odm.player.net.dll (
    copy /Y .\odm\odm.player\odm.player.net\bin\x64\Release\odm.player.net.dll build\
)

rem --- FFmpeg shared DLLs (LGPL build, x64) ---
copy /Y libs\ffmpeg-n7.1-lgpl-shared\x64\bin\*.dll build\

if not exist build\images mkdir build\images
copy /Y .\odm\odm.ui.views\images\wheel_zoom.cur build\images

if not exist build\locales mkdir build\locales
copy /Y .\odm\odm.localization\locales\*.* build\locales

if not exist build\meta mkdir build\meta
copy /Y .\odm\odm.ui.app\meta\*.* build\meta

if not exist build\logs mkdir build\logs
copy /Y .\odm\odm.ui.app\logs\*.* build\logs

rem --- Verify that the critical native DLL made it into build\ ---
if not exist build\odm.player.net.dll (
    echo ERROR: build\odm.player.net.dll is missing.
    echo Build the native player solution first:
    echo   msbuild odm\odm.player\odm.player.sln /p:Configuration=Release /p:Platform=x64 /p:SolutionDir=%%CD%%\
    exit /b 1
)

echo Package complete.
