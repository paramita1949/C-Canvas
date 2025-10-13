@echo off
setlocal EnableDelayedExpansion
title Canvas Cast - Launcher

echo.
echo ================================
echo        Canvas Cast
echo ================================
echo.

if exist "bin\Release\net8.0-windows\Canvas.exe" (
    echo Starting application (Release)...
    echo.
    start "" "bin\Release\net8.0-windows\Canvas.exe"
    timeout /t 2 >nul
) else if exist "bin\Debug\net8.0-windows\Canvas.exe" (
    echo Starting application (Debug)...
    echo.
    start "" "bin\Debug\net8.0-windows\Canvas.exe"
    timeout /t 2 >nul
) else (
    echo [ERROR] Executable not found
    echo Please run 'build.bat' first to compile the project
    echo.
    pause
)
