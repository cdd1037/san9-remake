[CmdletBinding()]
param(
    [string] $GodotPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = Join-Path $repoRoot 'src/San9.Client.Godot'
$outputRoot = Join-Path $repoRoot 'local/generated/export/windows-x64'
$template = Join-Path $repoRoot 'private/template/windows_release_x86_64.exe'

if (-not $GodotPath) {
    $editor = Get-Command 'Godot*_mono_win64.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $editor) { throw 'Pass -GodotPath with the Godot 4.7.2 .NET executable.' }
    $GodotPath = $editor.Source
}
$GodotPath = (Resolve-Path -LiteralPath $GodotPath).Path
foreach ($required in @($template,
    (Join-Path $projectRoot 'assets/LXGWWenKaiMonoGBLite-Medium.ttf'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing export input: $required" }
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Invoke-Godot([string[]] $Arguments, [string] $LogName) {
    $stdout = Join-Path $outputRoot "$LogName.log"
    $stderr = Join-Path $outputRoot "$LogName.errors.log"
    $commandArguments = @('--headless', '--path', ('"' + $projectRoot + '"')) + $Arguments
    $process = Start-Process -FilePath $GodotPath -ArgumentList $commandArguments -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    # Wait for the editor, not its reusable MSBuild worker processes.
    $process.WaitForExit()
    Get-Content -LiteralPath $stdout -Encoding utf8 | Write-Host
    Get-Content -LiteralPath $stderr -Encoding utf8 | Write-Host
    if ($process.ExitCode -ne 0) { throw "Godot $LogName failed ($($process.ExitCode)). See $stdout and $stderr." }
    if (Select-String -LiteralPath $stdout, $stderr -Pattern '^ERROR:' -Quiet) {
        throw "Godot $LogName reported errors despite a zero exit code. See $stdout and $stderr."
    }
}

Push-Location $repoRoot
try {
    Invoke-Godot @('--import') 'import'
    Invoke-Godot @('--export-release', '"Windows Desktop"', ('"' + (Join-Path $outputRoot 'San9.exe') + '"')) 'export'
    if (-not (Test-Path -LiteralPath (Join-Path $outputRoot 'San9.exe') -PathType Leaf)) { throw 'Godot did not produce San9.exe.' }

    Write-Host "Exported: $(Join-Path $outputRoot 'San9.exe')"
    Write-Host 'San9.exe includes the PCK and .NET build outputs. Converted content is not copied.'
} finally {
    Pop-Location
}
