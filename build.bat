@echo off
setlocal EnableDelayedExpansion
title Canvas Cast - Build Script

echo.
echo ================================
echo   Canvas Cast - Build Script
echo ================================
echo.

REM Check .NET SDK
echo [1/4] Checking .NET SDK...
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK not found
    echo.
    echo Please download and install .NET 8.0 SDK from:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

dotnet --version
echo.

REM Clean old files
echo [2/4] Cleaning old files...
dotnet clean >nul 2>&1
if exist bin\ rmdir /s /q bin
if exist obj\ rmdir /s /q obj
echo Cleaning completed
echo.

REM Restore packages
echo [3/4] Restoring packages...
dotnet restore ImageColorChanger.csproj
if %errorlevel% neq 0 (
    echo [ERROR] Package restore failed
    pause
    exit /b 1
)
echo Restore completed
echo.

REM Build project
echo [4/4] Building project...
dotnet build ImageColorChanger.csproj -c Release
if %errorlevel% neq 0 (
    echo [ERROR] Build failed
    pause
    exit /b 1
)
echo.

echo ================================
echo   Build Successful!
echo ================================
echo.
echo Output: bin\Release\net8.0-windows\Canvas Cast.exe
echo.
echo Run 'run.bat' to start the application
echo.
pause
