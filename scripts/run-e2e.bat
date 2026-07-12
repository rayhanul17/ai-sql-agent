@echo off
setlocal enabledelayedexpansion
REM ===========================================================================
REM  Double-click me: builds the app, starts it, runs the e2e smoke test,
REM  prints the result, then shuts the app down.
REM
REM  PROVIDER: 1 = Groq (needs a Groq key in appsettings), 0 = Ollama (local).
REM  Change the line below to switch. Groq is the reliable default.
REM ===========================================================================
set PROVIDER=1
set BASE_URL=http://localhost:5132
set APP_PROJECT=src\SqlAgent.Web
set STARTUP_TIMEOUT=60

REM --- move to the repo root (this .bat lives in scripts\) ---
cd /d "%~dp0\.."

REM --- locate bash.exe (Git for Windows) ---
set "BASH=C:\Program Files\Git\bin\bash.exe"
if not exist "%BASH%" set "BASH=C:\Program Files (x86)\Git\bin\bash.exe"
if not exist "%BASH%" (
  echo [ERROR] Git Bash not found. Install Git for Windows, or run scripts\e2e-smoke.sh manually.
  goto :end
)

echo === Building the app ===
dotnet build -c Debug --nologo -v q
if errorlevel 1 (
  echo [ERROR] Build failed. Fix the errors above and try again.
  goto :end
)

echo.
echo === Starting the app in the background ===
REM Start the web app in its own window so we can kill it afterwards by title.
start "AISQL-E2E-APP" /min cmd /c "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --no-build -c Debug --project %APP_PROJECT%"

echo Waiting for the app to be ready (up to %STARTUP_TIMEOUT%s)...
set /a WAITED=0
:waitloop
REM Poll the home page; curl ships with Windows 10+.
curl -s -o nul -w "%%{http_code}" %BASE_URL%/ 2>nul | findstr /r "200 302 404" >nul
if not errorlevel 1 goto :ready
set /a WAITED+=2
if !WAITED! geq %STARTUP_TIMEOUT% (
  echo [ERROR] App did not become ready within %STARTUP_TIMEOUT%s.
  goto :cleanup
)
timeout /t 2 /nobreak >nul
goto :waitloop

:ready
echo App is up.
echo.
echo === Running e2e smoke test (PROVIDER=%PROVIDER%) ===
echo.
"%BASH%" -c "PROVIDER=%PROVIDER% BASE_URL=%BASE_URL% bash scripts/e2e-smoke.sh"
set TEST_EXIT=%errorlevel%

:cleanup
echo.
echo === Stopping the app ===
REM Kill the background app window (by its title) and any stray dotnet run.
taskkill /fi "WINDOWTITLE eq AISQL-E2E-APP*" /t /f >nul 2>&1
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='SqlAgent.Web.exe'\" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }" >nul 2>&1

echo.
if "%TEST_EXIT%"=="0" (
  echo === ALL TESTS PASSED ===
) else (
  echo === SOME TESTS FAILED (exit %TEST_EXIT%^) ===
)

:end
echo.
echo Press any key to close this window...
pause >nul
endlocal
