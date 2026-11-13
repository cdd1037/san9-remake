param(
    [string]$ContentRoot,
    [string]$GodotPath,
    [switch]$CheckOnly,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$GameArguments
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dotnetRoot = Join-Path $repositoryRoot 'local/generated/dotnet'
$projectPath = Join-Path $repositoryRoot 'src/San9.Client.Godot'
if (-not $GodotPath) {
    $GodotPath = Join-Path $repositoryRoot 'local/generated/godot-4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe'
}
if (-not $ContentRoot) {
    $ContentRoot = Join-Path $repositoryRoot 'local/generated/equipment-upgrade-report-validation'
}
foreach ($path in @((Join-Path $dotnetRoot 'dotnet.exe'), $GodotPath, (Join-Path $projectPath 'project.godot'), (Join-Path $ContentRoot 'catalog.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required file not found: $path" }
}
$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = "$dotnetRoot;$env:PATH"
if ($CheckOnly) {
    Write-Host "Godot: $GodotPath"
    Write-Host "Content: $ContentRoot"
    & (Join-Path $dotnetRoot 'dotnet.exe') --list-runtimes
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 0
}
& $GodotPath --path $projectPath -- --content-root $ContentRoot @GameArguments
exit $LASTEXITCODE
