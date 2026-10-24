param(
    [Parameter(Mandatory = $true)]
    [string]$GamePath,
    [string]$DeployPath = (Join-Path $env:APPDATA 'MVL\Modpack\测试服务器\Mods')
)

$ErrorActionPreference = 'Stop'
$gameDirectory = (Resolve-Path -LiteralPath $GamePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameDirectory 'VintagestoryAPI.dll'))) {
    throw 'GamePath must point to a Vintage Story installation.'
}
$info = Get-Content (Join-Path $PSScriptRoot 'spyglass/modinfo.json') -Raw | ConvertFrom-Json
$releaseDirectory = Join-Path $PSScriptRoot 'Releases'
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$stageParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$stageDirectory = Join-Path $stageParent ('spyglassfork-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stageDirectory | Out-Null
try {
    & dotnet publish (Join-Path $PSScriptRoot 'spyglass/spyglass.csproj') -c Release --nologo `
        "-p:VINTAGE_STORY=$gameDirectory" '-p:DebugType=None' '-p:DebugSymbols=false' --output $stageDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Mod compilation failed.' }
    # The game supplies dependencies; package only the mod DLL, resources and notices.
    Remove-Item -LiteralPath (Join-Path $stageDirectory 'spyglassfork.deps.json') -ErrorAction SilentlyContinue
    $packagePath = Join-Path $releaseDirectory ($info.ModID + '_' + $info.version + '_VS1.22.3-1.22.7.zip')
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stageDirectory, $packagePath)
    $deployDirectory = (Resolve-Path -LiteralPath $DeployPath -ErrorAction SilentlyContinue)
    if ($null -eq $deployDirectory) {
        New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null
        $deployDirectory = Resolve-Path -LiteralPath $DeployPath
    }
    foreach ($pattern in @('spyglassfork_*.zip', 'Spyglass Fork-*.zip')) {
        Get-ChildItem -LiteralPath $deployDirectory.Path -Filter $pattern -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
    }
    Copy-Item -LiteralPath $packagePath -Destination (Join-Path $deployDirectory.Path ([System.IO.Path]::GetFileName($packagePath))) -Force
    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($packagePath + '.sha256') -Value ($hash + '  ' + [System.IO.Path]::GetFileName($packagePath)) -NoNewline
    Get-Item -LiteralPath $packagePath
    Get-FileHash -LiteralPath $packagePath -Algorithm SHA256
} finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stageDirectory)
    if ([System.IO.Path]::GetDirectoryName($resolvedStage) -ne $stageParent.TrimEnd('\', '/') `
        -or [System.IO.Path]::GetFileName($resolvedStage) -notlike 'spyglassfork-package-*') {
        throw 'Unexpected staging directory; refusing cleanup.'
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
