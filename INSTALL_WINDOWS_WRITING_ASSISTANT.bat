@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Windows Writing Assistant - Clean Install

set "INSTALL=%LOCALAPPDATA%\WindowsWritingAssistant"
set "BUILD=%TEMP%\WindowsWritingAssistantFreshBuild"

echo ============================================
echo   Windows Writing Assistant CLEAN INSTALL
echo ============================================
echo.
echo [1/5] Building the new Windows Writing Assistant first...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo ERROR: .NET 8 SDK was not found.
  echo Install the .NET 8 SDK, then run this installer again.
  pause
  exit /b 1
)
if exist "%BUILD%" rmdir /S /Q "%BUILD%" >nul 2>&1
dotnet publish WindowsWritingAssistant.csproj -c Release -r win-x64 --self-contained false -o "%BUILD%"
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Your currently installed Windows Writing Assistant was NOT removed.
  pause
  exit /b 1
)

echo [2/5] Closing every running old Windows Writing Assistant...
taskkill /F /IM WindowsWritingAssistant.exe >nul 2>&1
taskkill /F /IM WindowsWritingAssistant.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo [3/5] Removing old installed Windows Writing Assistant versions...
for %%D in (
  "%LOCALAPPDATA%\WindowsWritingAssistant"
  "%LOCALAPPDATA%\WindowsWritingAssistantApp"
  "%APPDATA%\Helper"
  "%APPDATA%\HelperApp"
  "%LOCALAPPDATA%\Programs\Helper"
  "%LOCALAPPDATA%\Programs\HelperApp"
) do if exist "%%~D" rmdir /S /Q "%%~D" >nul 2>&1

del /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Helper.lnk" >nul 2>&1
del /Q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Helper.bat" >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Helper /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v HelperApp /f >nul 2>&1

echo [4/5] Installing fresh Helper...
mkdir "%INSTALL%" >nul 2>&1
xcopy "%BUILD%\*" "%INSTALL%\" /E /I /Y >nul
rmdir /S /Q "%BUILD%" >nul 2>&1

> "%INSTALL%\START_HELPER.bat" echo @echo off
>>"%INSTALL%\START_HELPER.bat" echo start "" "%%~dp0WindowsWritingAssistant.exe"

echo [5/5] Starting Helper...
start "" "%INSTALL%\WindowsWritingAssistant.exe"

echo.
echo DONE - old installed Windows Writing Assistant versions were removed and replaced.
echo New install: %INSTALL%
timeout /t 3 /nobreak >nul
endlocal
