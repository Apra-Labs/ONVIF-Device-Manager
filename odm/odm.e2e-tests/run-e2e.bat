@echo off
setlocal enabledelayedexpansion
taskkill /F /IM odm.exe 2>nul
timeout /T 2 /nobreak >nul
set ODM_SMOKE_CONFIG=C:\odm-e2e-results\smoke-config.json
set VSTEST=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe
set TEST_DLL=C:\ak\ODM-Test\odm\odm.e2e-tests\bin\Release\odm.e2e-tests.dll
set ADAPTER=C:\ak\ODM-Test\packages\MSTest.TestAdapter.1.4.0\build\_common
set LOG=C:\odm-e2e-results\reports\run-%DATE:~-4%%DATE:~4,2%%DATE:~7,2%-%TIME:~0,2%%TIME:~3,2%%TIME:~6,2%.log
set LOG=%LOG: =0%
"%VSTEST%" "%TEST_DLL%" /TestAdapterPath:"%ADAPTER%" /logger:console;verbosity=detailed /logger:trx > "%LOG%" 2>&1
echo Exit code: !ERRORLEVEL! >> "%LOG%"
echo Log: %LOG%
