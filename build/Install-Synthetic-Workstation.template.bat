@echo off
setlocal EnableExtensions

if not defined UNC_SERVER_PATH set "UNC_SERVER_PATH=[UNC_SERVER_PATH]"
if not defined PROGRAMDATA_ROOT set "PROGRAMDATA_ROOT=C:\ProgramData"
if not defined PROGRAMFILES_ROOT set "PROGRAMFILES_ROOT=C:\Program Files"
if not defined REVIT_INSTALL_ROOT set "REVIT_INSTALL_ROOT=C:\Program Files\Autodesk"

if "%UNC_SERVER_PATH:~-1%"=="\" if not "%UNC_SERVER_PATH:~-2%"==":\" set "UNC_SERVER_PATH=%UNC_SERVER_PATH:~0,-1%"
if "%PROGRAMDATA_ROOT:~-1%"=="\" if not "%PROGRAMDATA_ROOT:~-2%"==":\" set "PROGRAMDATA_ROOT=%PROGRAMDATA_ROOT:~0,-1%"
if "%PROGRAMFILES_ROOT:~-1%"=="\" if not "%PROGRAMFILES_ROOT:~-2%"==":\" set "PROGRAMFILES_ROOT=%PROGRAMFILES_ROOT:~0,-1%"
if "%REVIT_INSTALL_ROOT:~-1%"=="\" if not "%REVIT_INSTALL_ROOT:~-2%"==":\" set "REVIT_INSTALL_ROOT=%REVIT_INSTALL_ROOT:~0,-1%"

if not exist "%UNC_SERVER_PATH%\Synthetic\Payload\" (
    echo ERROR: Staged UNC payload directory not found: "%UNC_SERVER_PATH%\Synthetic\Payload"
    exit /b 16
)

REM 1. Purge retired Revit 2022 manifest and legacy flat ProgramData\...\Synthetic root artifacts
set "LEGACY_ADDIN_2022=%PROGRAMDATA_ROOT%\Autodesk\Revit\Addins\2022\Synthetic2022.addin"
if exist "%LEGACY_ADDIN_2022%" del /f /q "%LEGACY_ADDIN_2022%"

set "LEGACY_SYNTHETIC_ROOT=%PROGRAMDATA_ROOT%\Autodesk\Revit\Addins\Synthetic"
if exist "%LEGACY_SYNTHETIC_ROOT%\" (
    if exist "%LEGACY_SYNTHETIC_ROOT%\Synthetic202*.*" del /f /q "%LEGACY_SYNTHETIC_ROOT%\Synthetic202*.*"
    if exist "%LEGACY_SYNTHETIC_ROOT%\Newtonsoft.Json.*" del /f /q "%LEGACY_SYNTHETIC_ROOT%\Newtonsoft.Json.*"
    if exist "%LEGACY_SYNTHETIC_ROOT%\SyntheticSettings*.json" del /f /q "%LEGACY_SYNTHETIC_ROOT%\SyntheticSettings*.json"
    if exist "%LEGACY_SYNTHETIC_ROOT%\SyntheticWorksets.xlsx" del /f /q "%LEGACY_SYNTHETIC_ROOT%\SyntheticWorksets.xlsx"
    if exist "%LEGACY_SYNTHETIC_ROOT%\INC Worksets.xlsx" del /f /q "%LEGACY_SYNTHETIC_ROOT%\INC Worksets.xlsx"
    if exist "%LEGACY_SYNTHETIC_ROOT%\MergeDuplicates_Step6_ExecutionResult.json" del /f /q "%LEGACY_SYNTHETIC_ROOT%\MergeDuplicates_Step6_ExecutionResult.json"
    if exist "%LEGACY_SYNTHETIC_ROOT%\Assets\" rmdir /s /q "%LEGACY_SYNTHETIC_ROOT%\Assets"
)

REM 2. Stage Bootstrap/v1, Payload/<Year>, and .addin manifests for installed Revit versions
for /d %%D in ("%UNC_SERVER_PATH%\Synthetic\Payload\*") do (
    call :install_year "%%~nxD"
    if errorlevel 8 goto :fail
)

if not defined NO_PAUSE pause
exit /b 0

:install_year
set "YEAR=%~1"
echo %YEAR%| findstr /r "^[0-9][0-9][0-9][0-9]$" >nul
if errorlevel 1 exit /b 0

if not exist "%REVIT_INSTALL_ROOT%\Revit %YEAR%\" exit /b 0

if %YEAR% LSS 2027 (
    set "TARGET_ADDINS_ROOT=%PROGRAMDATA_ROOT%\Autodesk\Revit\Addins"
) else (
    set "TARGET_ADDINS_ROOT=%PROGRAMFILES_ROOT%\Autodesk\Revit\Addins"
)

REM Copy Release .addin manifest
robocopy "%UNC_SERVER_PATH%\Manifest for Deploy" "%TARGET_ADDINS_ROOT%\%YEAR%" "Synthetic%YEAR%.addin" /R:3 /W:2 /NP
if errorlevel 8 exit /b %ERRORLEVEL%

REM Seed Bootstrap/v1 with skip-existing flags so active locked binaries are never overwritten
robocopy "%UNC_SERVER_PATH%\Synthetic\Bootstrap\v1" "%TARGET_ADDINS_ROOT%\Synthetic\Bootstrap\v1" "Synthetic.Bootstrap%YEAR%.*" /XC /XN /XO /R:3 /W:2 /NP
if errorlevel 8 exit /b %ERRORLEVEL%

REM Mirror Payload/<Year> for Stage-While-Open updates
robocopy "%UNC_SERVER_PATH%\Synthetic\Payload\%YEAR%" "%TARGET_ADDINS_ROOT%\Synthetic\Payload\%YEAR%" /MIR /R:3 /W:2 /NP
if errorlevel 8 exit /b %ERRORLEVEL%

exit /b 0

:fail
exit /b %ERRORLEVEL%
