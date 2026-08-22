@echo off
REM v1.0.0
REM Metlab GitHub WikiのMarkdown変更を公開するPowerShellランチャー。

chcp 65001 > nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Publish-MetlabWiki.ps1"
if errorlevel 1 (
  echo.
  echo Wiki公開処理でエラーが発生しました。表示内容を確認してください。
)
pause

