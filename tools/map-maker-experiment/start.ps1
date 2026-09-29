[CmdletBinding()]
param([string] $GodotPath, [string] $TexturePackage, [string] $Project, [switch] $Verify)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $TexturePackage) { $TexturePackage = Join-Path $repositoryRoot 'local/generated/content/textures-san9pk-tc101' }
$TexturePackage = (Resolve-Path -LiteralPath $TexturePackage).Path
if (-not (Test-Path -LiteralPath (Join-Path $TexturePackage 'textures.json'))) { throw 'Select a converted texture package containing textures.json.' }
if (-not $GodotPath) {
    $godotCommand = Get-Command 'Godot*_mono_win64.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $godotCommand) { throw 'Pass -GodotPath with the Godot .NET executable.' }
    $GodotPath = $godotCommand.Source
}
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
& $dotnetPath build (Join-Path $PSScriptRoot 'San9.TerrainEditor.csproj')
if ($LASTEXITCODE -ne 0) { throw 'Editor build failed.' }
$evidenceRoot = Join-Path $repositoryRoot 'local/generated/terrain-editor'
New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
$arguments = @('--path', ('"' + $PSScriptRoot + '"'), '--', '--texture-package', ('"' + $TexturePackage + '"'))
if ($Verify) { $arguments += '--verify' }
if ($Project) { $arguments += @('--project', ('"' + (Resolve-Path -LiteralPath $Project).Path + '"')) }
$logName = if ($Verify) { 'verify' } else { 'run' }
$windowStyle = if ($Verify) { 'Hidden' } else { 'Normal' }
$editorProcess = Start-Process -FilePath $GodotPath -ArgumentList $arguments -WindowStyle $windowStyle -PassThru `
    -RedirectStandardOutput (Join-Path $evidenceRoot "$logName.log") -RedirectStandardError (Join-Path $evidenceRoot "$logName.errors.log")
if ($Verify) {
    if (-not $editorProcess.WaitForExit(60000)) {
        Stop-Process -Id $editorProcess.Id
        throw 'Editor verification timed out.'
    }
    Get-Content -LiteralPath (Join-Path $evidenceRoot "$logName.log") -Encoding utf8
    Get-Content -LiteralPath (Join-Path $evidenceRoot "$logName.errors.log") -Encoding utf8
    if ($editorProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath (Join-Path $evidenceRoot "$logName.log") -Pattern '^TERRAIN_EDITOR_VERIFY_OK' -Quiet)) {
        throw 'Editor verification failed.'
    }
} else {
    Write-Host "Terrain editor started (PID $($editorProcess.Id))."
}
