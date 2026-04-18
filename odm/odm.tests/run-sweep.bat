@echo off
setlocal

:: Paths -- override from command line if needed
::   run-sweep.bat [ODM_ROOT] [REPORT_DIR] [CRED_SRC]
set ODM_ROOT=%~dp0..\..
if not "%~1"=="" set ODM_ROOT=%~1

set TEST_BIN=%ODM_ROOT%\odm\odm.tests\bin\x64\Release\net48
set CRED_SRC=C:\Program Files\Synesis\Onvif Device Manager\config\credentials.dat
if not "%~3"=="" set CRED_SRC=%~3

set VSTEST="C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"

if not "%~2"=="" set ODM_SWEEP_REPORT_DIR=%~2

:: Ensure config dir exists
if not exist "%TEST_BIN%\config\" mkdir "%TEST_BIN%\config"

:: Always copy (overwrite) credentials.dat so the latest file is used every run
echo Copying credentials.dat from "%CRED_SRC%"...
copy /y "%CRED_SRC%" "%TEST_BIN%\config"
if errorlevel 1 (
    echo ERROR: Could not copy credentials.dat.
    echo   - Source: "%CRED_SRC%"
    echo   - Pass a custom path as third arg: run-sweep.bat "" "" "C:\path\to\credentials.dat"
    pause
    exit /b 1
)

echo Running camera compatibility sweep...
echo.

%VSTEST% "%TEST_BIN%\odm.tests.dll" /TestCaseFilter:"TestCategory=Sweep" /Platform:x64

echo.
:: Print the full path of the generated report
for /f "delims=" %%F in ('dir /b /od "%TEST_BIN%\camera-sweep-*.md" 2^>nul') do set LAST_REPORT=%%F
if defined LAST_REPORT (
    echo Report: %TEST_BIN%\%LAST_REPORT%
) else (
    echo No report file found in %TEST_BIN%
)
pause
