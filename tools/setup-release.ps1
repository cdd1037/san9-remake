[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('windows', 'linux', 'macos')]
    [string] $Platform,
    [long] $TemplateReleaseId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$toolsRoot = Join-Path $repoRoot 'local/generated/release-tools'
$templateRoot = Join-Path $toolsRoot 'templates/current'
$editorRoot = Join-Path $toolsRoot "editors/4.7.2/$Platform"
$releaseEndpoint = if ($TemplateReleaseId) { "$TemplateReleaseId" } else { 'latest' }
$releaseHeaders = @{}
if ($env:GH_TOKEN) {
    $releaseHeaders.Authorization = "Bearer $env:GH_TOKEN"
}
$templateRelease = Invoke-RestMethod -Uri "https://api.github.com/repos/cdd1037/godot-template/releases/$releaseEndpoint" -Headers $releaseHeaders
Write-Host "Export templates: $($templateRelease.tag_name) ($($templateRelease.html_url))"
$editorUrl = 'https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable'
$targets = @{
    windows = @{
        Archive = 'Godot_v4.7.2-stable_mono_win64.zip'
        Hash = 'a2a48473a7414c5f19fab690518caebb738c09ef9601f6bd2388676a7f53b3c0'
        Executable = 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe'
        Templates = @('windows_release_x86_64.exe', 'windows_release_x86_64.console.exe')
    }
    linux = @{
        Archive = 'Godot_v4.7.2-stable_mono_linux_x86_64.zip'
        Hash = '129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247'
        Executable = 'Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64'
        Templates = @('linux_release.x86_64')
    }
    macos = @{
        Archive = 'Godot_v4.7.2-stable_mono_macos.universal.zip'
        Hash = '8af3977b60d2c59802f7c8ff1914b3ca02a5e294f7381fc1104ee777e33cbbd8'
        Executable = 'Godot_mono.app/Contents/MacOS/Godot'
        Templates = @('macos_release.arm64.mono.zip')
    }
}

function Get-ReleaseAsset([string] $Url, [string] $Destination, [string] $Sha256) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) {
        Write-Host "Downloading: $Url"
        $temporary = "$Destination.download"
        Invoke-WebRequest -Uri $Url -OutFile $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $Sha256) {
            throw "SHA256 mismatch: $Url"
        }
        Move-Item -LiteralPath $temporary -Destination $Destination -Force
    }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ne $Sha256) {
        throw "SHA256 mismatch in existing file: $Destination"
    }
}

$target = $targets[$Platform]
foreach ($name in $target.Templates) {
    $assets = @($templateRelease.assets | Where-Object name -CEQ $name)
    if ($assets.Count -ne 1 -or $assets[0].digest -cnotmatch '^sha256:([0-9a-f]{64})$') {
        throw "Missing template or SHA256 digest in release $($templateRelease.tag_name): $name"
    }
    $sha256 = $Matches[1]
    Get-ReleaseAsset $assets[0].browser_download_url (Join-Path $templateRoot $name) $sha256
}
Get-ReleaseAsset 'https://github.com/lxgw/LxgwWenkaiGB-Lite/releases/download/v1.522/LXGWWenKaiMonoGBLite-Medium.ttf' `
    (Join-Path $repoRoot 'src/San9.Client.Godot/assets/LXGWWenKaiMonoGBLite-Medium.ttf') `
    '7ff485f8c27ed74109bf3929849756017c443330ed8d0821487e38dad3bd2591'

$archive = Join-Path $editorRoot $target.Archive
Get-ReleaseAsset "$editorUrl/$($target.Archive)" $archive $target.Hash
$executable = Join-Path $editorRoot $target.Executable
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    if ($IsWindows) { Expand-Archive -LiteralPath $archive -DestinationPath $editorRoot -Force }
    elseif ($IsMacOS) { & ditto -x -k $archive $editorRoot; if ($LASTEXITCODE -ne 0) { throw 'Editor extraction failed.' } }
    else { & unzip -q $archive -d $editorRoot; if ($LASTEXITCODE -ne 0) { throw 'Editor extraction failed.' } }
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing editor executable: $executable" }
if (-not $IsWindows) { & chmod +x $executable; if ($LASTEXITCODE -ne 0) { throw 'Setting editor executable permission failed.' } }
if ($Platform -eq 'linux' -and -not $IsWindows) {
    & chmod +x (Join-Path $templateRoot 'linux_release.x86_64')
    if ($LASTEXITCODE -ne 0) { throw 'Setting template executable permission failed.' }
}

$licenseRoot = Join-Path $toolsRoot 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
foreach ($name in @('LICENSE.txt', 'COPYRIGHT.txt')) {
    $destination = Join-Path $licenseRoot "GODOT-$name"
    if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
        Invoke-WebRequest -Uri "https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf001b61586d9930840f2f1394092c079/$name" -OutFile $destination
    }
}
# The only success-stream output is the executable path, for local use and GITHUB_OUTPUT.
$executable
