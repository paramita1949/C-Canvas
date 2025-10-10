@echo off
chcp 65001 >nul
title 图片变色工具 - 编译脚本

echo.
echo ================================
echo   图片变色工具 - 编译脚本
echo   WPF + Material Design
echo ================================
echo.

:: 检查.NET SDK
echo 正在检查.NET版本...
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [错误] 未找到 .NET SDK
    echo.
    echo 请从以下地址下载并安装 .NET 8.0 SDK:
    echo https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

dotnet --version
echo.

:: 清理旧文件
echo 正在清理旧文件...
dotnet clean >nul 2>&1
if exist "bin\" rmdir /s /q "bin"
if exist "obj\" rmdir /s /q "obj"
echo 清理完成
echo.

:: 还原依赖包
echo 正在还原依赖包...
dotnet restore ImageColorChanger.csproj
if errorlevel 1 (
    echo [错误] 依赖包还原失败
    pause
    exit /b 1
)
echo 依赖包还原完成
echo.

:: 编译项目
echo 正在编译项目...
dotnet build ImageColorChanger.csproj -c Release
if errorlevel 1 (
    echo [错误] 编译失败
    pause
    exit /b 1
)
echo.

echo ================================
echo        编译成功！
echo ================================
echo.
echo 可执行文件位置：
echo bin\Release\net8.0-windows\ImageColorChanger.exe
echo.
echo 提示：可以运行 run.bat 来启动程序
echo.
pause
