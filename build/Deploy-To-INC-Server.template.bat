@echo off
setlocal EnableExtensions

if not defined LOCAL_OUTPUT_PATH set "LOCAL_OUTPUT_PATH=[LOCAL_OUTPUT_PATH]"
if not defined UNC_SERVER_PATH set "UNC_SERVER_PATH=[UNC_SERVER_PATH]"

if "%LOCAL_OUTPUT_PATH:~-1%"=="\" if not "%LOCAL_OUTPUT_PATH:~-2%"==":\" set "LOCAL_OUTPUT_PATH=%LOCAL_OUTPUT_PATH:~0,-1%"
if "%UNC_SERVER_PATH:~-1%"=="\" if not "%UNC_SERVER_PATH:~-2%"==":\" set "UNC_SERVER_PATH=%UNC_SERVER_PATH:~0,-1%"

REM 1. Mirror Bootstrap/v1 and Payload/<Year> outputs to UNC share (exclude .addin manifests)
robocopy "%LOCAL_OUTPUT_PATH%\Synthetic" "%UNC_SERVER_PATH%\Synthetic" /MIR /XF *.addin /R:3 /W:2 /NP
if errorlevel 8 exit /b %ERRORLEVEL%

REM 2. Copy generated Release .addin manifests to UNC Manifest for Deploy
robocopy "%LOCAL_OUTPUT_PATH%\Manifest for Deploy" "%UNC_SERVER_PATH%\Manifest for Deploy" *.addin /R:3 /W:2 /NP
if errorlevel 8 exit /b %ERRORLEVEL%

REM 3. Overlay studio INC Settings into every Payload/<Year> directory without /MIR
for /d %%D in ("%UNC_SERVER_PATH%\Synthetic\Payload\*") do (
    call :overlay_year_settings "%%~D" "%%~nxD"
    if errorlevel 8 goto :fail
)

if not defined NO_PAUSE pause
exit /b 0

:overlay_year_settings
echo %~2| findstr /r "^[0-9][0-9][0-9][0-9]$" >nul
if errorlevel 1 exit /b 0
robocopy "%LOCAL_OUTPUT_PATH%\INC Settings" "%~1" /E /R:3 /W:2 /NP
if errorlevel 8 exit /b %ERRORLEVEL%
exit /b 0

:fail
exit /b %ERRORLEVEL%
