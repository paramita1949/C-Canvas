@echo off
title Canvas Cast - Launcher

echo.
echo ================================
echo        Canvas Cast
echo ================================
echo.

echo Starting application...
echo.

cd /d "bin\Release\net8.0-windows"
if exist "CanvasCast.exe" (
    start "" "CanvasCast.exe"
    goto :end
)

cd /d "%~dp0"
cd /d "bin\Debug\net8.0-windows"
if exist "CanvasCast.exe" (
    start "" "CanvasCast.exe"
    goto :end
)

echo [ERROR] Executable not found
echo Please run 'build.bat' first to compile the project
pause

:end
cd /d "%~dp0"