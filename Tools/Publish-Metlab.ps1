# v1.3.0
# Metlabのバージョン更新、変更履歴、Git commit・tag・pushを一括実行する公開補助スクリプト。

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packagePaths = @(
    (Join-Path $repositoryRoot 'Packages\com.metlab.worlds\package.json'),
    (Join-Path $repositoryRoot 'Packages\com.metlab.avatars\package.json')
)

Set-Location -LiteralPath $repositoryRoot

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Gitが見つかりません。'
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Gitコマンドに失敗しました: git $($Arguments -join ' ')"
    }
}

$worldManifest = Get-Content -LiteralPath $packagePaths[0] -Raw | ConvertFrom-Json
$currentVersion = [version]$worldManifest.version
$suggestedVersion = '{0}.{1}.{2}' -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1)
$newVersion = Read-Host "新しいバージョン番号（Enterで$suggestedVersion）"
if ([string]::IsNullOrWhiteSpace($newVersion)) { $newVersion = $suggestedVersion }
if ($newVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'バージョンは例: 1.0.1 の形式で入力してください。' }
git rev-parse --verify --quiet "refs/tags/v$newVersion" | Out-Null
if ($LASTEXITCODE -eq 0) { throw "タグ v$newVersion は既に存在します。" }

$releaseNote = Read-Host '更新内容を1行で入力'
if ([string]::IsNullOrWhiteSpace($releaseNote)) { throw '更新内容は必須です。' }

foreach ($packagePath in $packagePaths) {
    $manifest = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
    $manifest.version = $newVersion
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $packagePath -Encoding utf8
}

$listingPath = Join-Path $repositoryRoot 'Website\index.json'
$listing = Get-Content -LiteralPath $listingPath -Raw | ConvertFrom-Json
foreach ($packagePath in $packagePaths) {
    $manifest = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
    $releaseManifest = $manifest.PSObject.Copy()
    $releaseManifest | Add-Member -NotePropertyName url -NotePropertyValue "https://github.com/MetraZero/Metlab/releases/download/v$newVersion/$($manifest.name)-$newVersion.zip"
    $versions = $listing.packages.($manifest.name).versions
    $versions | Add-Member -NotePropertyName $newVersion -NotePropertyValue $releaseManifest
}
$listing | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $listingPath -Encoding utf8

$changeLogPath = Join-Path $repositoryRoot 'CHANGELOG.md'
$oldChangeLog = Get-Content -LiteralPath $changeLogPath -Raw
$date = Get-Date -Format 'yyyy-MM-dd'
$newEntry = "# 変更履歴`r`n`r`n## $newVersion - $date`r`n`r`n- $releaseNote`r`n`r`n"
$body = $oldChangeLog -replace '^# 変更履歴\r?\n\r?\n', ''
Set-Content -LiteralPath $changeLogPath -Value ($newEntry + $body) -Encoding utf8

Invoke-Git -Arguments @('add', '--all')
Invoke-Git -Arguments @('commit', '-m', "Release v$newVersion - $releaseNote")
Invoke-Git -Arguments @('tag', '-a', "v$newVersion", '-m', "Metlab v$newVersion")
Invoke-Git -Arguments @('push', 'origin', 'main')
Invoke-Git -Arguments @('push', 'origin', "v$newVersion")

Write-Host "公開処理を開始しました: v$newVersion" -ForegroundColor Green
Write-Host 'GitHub Actionsの完了後、VCCに更新が表示されます。'
