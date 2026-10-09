@echo off
rem StoreOS Local Development Runner (Windows Command Prompt)
setlocal

where python >nul 2>nul
if %ERRORLEVEL% equ 0 (
    python "%~dp0run.py" %*
    exit /b %ERRORLEVEL%
)

where python3 >nul 2>nul
if %ERRORLEVEL% equ 0 (
    python3 "%~dp0run.py" %*
    exit /b %ERRORLEVEL%
)

where py >nul 2>nul
if %ERRORLEVEL% equ 0 (
    py "%~dp0run.py" %*
    exit /b %ERRORLEVEL%
)

echo [error] Python 3 is required to run StoreOS locally.
echo Please install Python 3 from https://www.python.org/downloads/ and add it to PATH.
exit /b 1
