@echo off
chcp 65001 >nul
title 图片变色工具 - 启动

echo.
echo ================================
echo    图片变色工具 - Canvas Cast
echo ================================
echo.

if exist "bin\Release\net8.0-windows\ImageColorChanger.exe" (
    echo 正在启动程序...
    echo.
    start "" "bin\Release\net8.0-windows\ImageColorChanger.exe"
    timeout /t 2 >nul
) else (
    echo [错误] 找不到可执行文件
    echo 请先运行 build.bat 编译项目
    echo.
    pause
)
