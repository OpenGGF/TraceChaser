@echo off
REM Record a BizHawk trace for any Sonic 1 zone/act.
REM The Lua script auto-detects zone and act from RAM.
REM
REM Usage:  set OGGF_TRACE_OUTPUT_DIR=<external-dir> then record_trace.bat <rom_path> <bk2_path>
REM Example: record_trace.bat "s1.gen" "Movies\s1-mz1.bk2"
REM
REM OGGF_TRACE_OUTPUT_DIR is mandatory and must identify external scratch.
REM
REM BizHawk path can be overridden with BIZHAWK_EXE env var.
REM To see the emulator window during recording, edit HEADLESS_VISIBLE in
REM s1_trace_recorder.lua (set to true).

setlocal

set "LUA_SCRIPT=%~dp0s1_trace_recorder.lua"

if "%OGGF_TRACE_OUTPUT_DIR%"=="" (
    echo ERROR: set OGGF_TRACE_OUTPUT_DIR to an explicit external directory.
    exit /b 1
)
if "%OGGF_INPUT_REPOSITORY_ROOT%"=="" (
    echo ERROR: set OGGF_INPUT_REPOSITORY_ROOT to the explicit consumer checkout.
    exit /b 1
)
for %%I in ("%OGGF_TRACE_OUTPUT_DIR%") do set "OUTPUT_DIR=%%~fI"
for %%I in ("%~dp0..") do set "OGGF_TRACECHASER_ROOT=%%~fI"
set "OUTPUT_GUARD=%~dp0assert_external_output.ps1"
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%OUTPUT_GUARD%" -TraceChaserRoot "%OGGF_TRACECHASER_ROOT%" -InputRepositoryRoot "%OGGF_INPUT_REPOSITORY_ROOT%" -OutputRoot "%OUTPUT_DIR%" >NUL
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
set "COMPRESS_SCRIPT=%~dp0..\traces\compress-traces.ps1"

if "%~1"=="" (
    echo Usage: %~nx0 ^<rom_path^> ^<bk2_path^>
    echo.
    echo   rom_path   Path to Sonic 1 REV01 ROM
    echo   bk2_path   Path to BK2 movie file
    echo.
    echo The script auto-detects zone and act from the game's RAM.
    echo Output is written to: %OUTPUT_DIR%\
    exit /b 1
)
if "%~2"=="" (
    echo Usage: %~nx0 ^<rom_path^> ^<bk2_path^>
    exit /b 1
)

for %%I in ("%~1") do set "ROM_PATH=%%~fI"
for %%I in ("%~2") do set "BK2_PATH=%%~fI"

echo === BizHawk Trace Recorder ===
echo ROM:    %ROM_PATH%
echo Movie:  %BK2_PATH%
echo Lua:    %LUA_SCRIPT%
echo Output: %OUTPUT_DIR%\
echo.
echo Starting BizHawk through reusable no-audio/no-render launcher...

call "%~dp0run_bizhawk_lua.bat" "%LUA_SCRIPT%" "%BK2_PATH%" "%ROM_PATH%"

if %ERRORLEVEL% neq 0 (
    echo BizHawk exited with error code %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

echo.
echo === Trace recording complete ===
if exist "%OUTPUT_DIR%\metadata.json" (
    set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
    "%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%COMPRESS_SCRIPT%" "%OUTPUT_DIR%"
    if %ERRORLEVEL% neq 0 (
        echo Trace compression failed with error code %ERRORLEVEL%
        exit /b %ERRORLEVEL%
    )
    echo Output files:
    dir /b "%OUTPUT_DIR%\"
    echo.
    REM Show metadata summary
    type "%OUTPUT_DIR%\metadata.json"
) else (
    echo WARNING: No trace output found in %OUTPUT_DIR%\
    echo Check BizHawk Lua console for errors.
)
