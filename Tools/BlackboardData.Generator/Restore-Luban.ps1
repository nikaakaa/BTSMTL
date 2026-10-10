param()

$ErrorActionPreference = 'Stop'
$dependency = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'luban.lock.json') -Encoding UTF8 -Raw | ConvertFrom-Json
$dependencyRoot = Join-Path $PSScriptRoot ('.tools\luban-' + $dependency.version)
$entryPath = Join-Path $dependencyRoot 'Luban\Luban.dll'
if (Test-Path -LiteralPath $entryPath) {
    Write-Output $entryPath
    return
}

[System.IO.Directory]::CreateDirectory($dependencyRoot) | Out-Null
$archivePath = Join-Path $dependencyRoot 'Luban.7z'
Invoke-WebRequest -Uri $dependency.archiveUrl -OutFile $archivePath
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($archiveHash -ne $dependency.archiveSha256) {
    throw "Luban $($dependency.version) 发布包校验和不匹配：$archiveHash"
}
& tar -xf $archivePath -C $dependencyRoot
if ($LASTEXITCODE -ne 0) {
    throw "Luban $($dependency.version) 发布包解压失败。"
}
Write-Output $entryPath
