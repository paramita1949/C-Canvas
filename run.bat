@echo off
chcp 65001 >nul
title Canvas Cast - 启动

echo.
echo ================================
echo        Canvas Cast
echo    专业的图片浏览与管理工具
echo ================================
echo.

if exist "bin\Release\net8.0-windows\Canvas.exe" (
    echo 正在启动程序...
    echo.
    start "" "bin\Release\net8.0-windows\Canvas.exe"
    timeout /t 2 >nul
) else if exist "bin\Debug\net8.0-windows\Canvas.exe" (
    echo 正在启动程序（Debug版本）...
    echo.
    start "" "bin\Debug\net8.0-windows\Canvas.exe"
    timeout /t 2 >nul
) else (
    echo [错误] 找不到可执行文件
    echo 请先运行 build.bat 编译项目
    echo.
    pause
)
