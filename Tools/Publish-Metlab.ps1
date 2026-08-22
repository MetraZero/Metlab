# v1.0.0
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

$worldManifest = Get-Content -LiteralPath $packagePaths[0] -Raw | ConvertFrom-Json
$currentVersion = [version]$worldManifest.version
$suggestedVersion = '{0}.{1}.{2}' -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1)
$newVersion = Read-Host "新しいバージョン番号（Enterで$suggestedVersion）"
if ([string]::IsNullOrWhiteSpace($newVersion)) { $newVersion = $suggestedVersion }
if ($newVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'バージョンは例: 1.0.1 の形式で入力してください。' }
if (git rev-parse "v$newVersion" 2>$null) { throw "タグ v$newVersion は既に存在します。" }

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

git add --all
git commit -m "Release v$newVersion - $releaseNote"
git tag -a "v$newVersion" -m "Metlab v$newVersion"
git push origin main
git push origin "v$newVersion"

Write-Host "公開処理を開始しました: v$newVersion" -ForegroundColor Green
Write-Host 'GitHub Actionsの完了後、VCCに更新が表示されます。'
