@echo off
REM v1.0.0
REM Metlabの公開補助PowerShellをダブルクリックで実行するランチャー。

chcp 65001 > nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Publish-Metlab.ps1"
if errorlevel 1 (
  echo.
  echo 公開処理でエラーが発生しました。表示内容を確認してください。
)
pause
