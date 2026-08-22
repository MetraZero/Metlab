# v1.0.0
# D:\Unity\Metlab.wiki内のMarkdown変更をGitHub Wikiへcommit・pushする公開補助スクリプト。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$wikiRoot = "$repositoryRoot.wiki"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Gitが見つかりません。'
}

if (-not (Test-Path -LiteralPath (Join-Path $wikiRoot '.git'))) {
    throw "Wiki管理用フォルダが見つかりません: $wikiRoot"
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & git -C $wikiRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Gitコマンドに失敗しました: git $($Arguments -join ' ')"
    }
}

Invoke-Git -Arguments @('fetch', 'origin', 'master')

# ページ削除は安全のため自動では公開せず、追加・変更されたMarkdownだけを対象にする。
& git -C $wikiRoot add --ignore-removal -- '*.md'
if ($LASTEXITCODE -ne 0) {
    throw 'Wiki Markdownのステージに失敗しました。'
}

& git -C $wikiRoot diff --cached --quiet
$hasChanges = ($LASTEXITCODE -eq 1)
if ($LASTEXITCODE -gt 1) {
    throw 'Wikiの変更確認に失敗しました。'
}

if (-not $hasChanges) {
    Invoke-Git -Arguments @('pull', '--ff-only', 'origin', 'master')
    Write-Host '公開するWiki変更はありません。最新版へ更新しました。' -ForegroundColor Green
    return
}

$updateNote = Read-Host 'Wikiの更新内容を1行で入力'
if ([string]::IsNullOrWhiteSpace($updateNote)) {
    throw '更新内容は必須です。'
}

Invoke-Git -Arguments @('commit', '-m', $updateNote)
Invoke-Git -Arguments @('pull', '--rebase', 'origin', 'master')
Invoke-Git -Arguments @('push', 'origin', 'master')

$deletedPages = & git -C $wikiRoot status --short -- '*.md' | Where-Object { $_ -match '^ D|^D ' }
if ($deletedPages) {
    Write-Warning '削除されたページは安全のため自動公開していません。削除を反映する場合は内容を確認してください。'
}

Write-Host 'GitHub Wikiを公開しました。' -ForegroundColor Green

