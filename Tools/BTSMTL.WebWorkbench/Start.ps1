param(
    [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
    [string]$AnimationLibrary,
    [int]$Port = 5367
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$processTemp = Join-Path $repositoryRoot '.codex/tmp/system-temp'
$buildArtifacts = Join-Path $repositoryRoot '.codex/tmp/web-workbench/build'
New-Item -ItemType Directory -Force -Path $processTemp | Out-Null
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$env:TEMP = $processTemp
$env:TMP = $processTemp

try {
    Push-Location $PSScriptRoot
    try {
        npm ci --ignore-scripts --no-audit --no-fund --cache (Join-Path $repositoryRoot '.codex/tmp/web-workbench/npm-cache')
        if ($LASTEXITCODE -ne 0) { throw '网页依赖安装失败，服务未启动。' }
    } finally {
        Pop-Location
    }
    try {
        dotnet build (Join-Path $PSScriptRoot 'BTSMTL.WebWorkbench.csproj') --configuration Release --disable-build-servers /nr:false /p:UseSharedCompilation=false /m:1 /p:UseArtifactsOutput=true "/p:ArtifactsPath=$buildArtifacts"
        $buildExit = $LASTEXITCODE
    } finally {
        dotnet build-server shutdown
    }
    if ($buildExit -ne 0) {
        throw '网页工作台编译失败，服务未启动。'
    }
    $serviceArguments = @('--urls', "http://127.0.0.1:$Port", '--contentRoot', $PSScriptRoot,
        '--Workspace', [IO.Path]::GetFullPath($WorkspaceRoot))
    if ($AnimationLibrary) { $serviceArguments += @('--AnimationLibrary', [IO.Path]::GetFullPath($AnimationLibrary)) }
    dotnet (Join-Path $buildArtifacts 'bin/BTSMTL.WebWorkbench/release/BTSMTL.WebWorkbench.dll') @serviceArguments
    if ($LASTEXITCODE -ne 0) {
        throw '网页工作台服务退出，原因见上方日志。'
    }
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
}
