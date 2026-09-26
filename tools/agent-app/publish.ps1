# Builds the Agent App for the agents' laptops, pointed at the server.
#
#   powershell -ExecutionPolicy Bypass -File tools\agent-app\publish.ps1
#   powershell -ExecutionPolicy Bypass -File tools\agent-app\publish.ps1 -Server http://192.168.1.100
#
# Writes publish\agent-app-<version>\ (the folder the laptops run) and
# publish\SmashedAgentApp-<version>.zip (the same folder, to carry over).
#
# The app has no screen for the server address: it reads Server:BaseUrl from
# the appsettings.json beside the program. The repository's copy says
# localhost:5000, for development, so this script writes the real address into
# the published copy only. Development keeps localhost.
#
# The version is the latest git tag (v0.2.2 -> 0.2.2), so the laptops' app
# matches the server release it was built beside.

param(
    [string]$Server = "http://192.168.1.100",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $repo

if (-not $Version) {
    $tag = git describe --tags --abbrev=0
    if (-not $tag) { throw "No git tag to take the version from; pass -Version 1.2.3" }
    $Version = $tag.TrimStart("v")
}

$out = Join-Path $repo "publish\agent-app-$Version"
$zip = Join-Path $repo "publish\SmashedAgentApp-$Version.zip"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
if (Test-Path $zip) { Remove-Item -Force $zip }

Write-Host "Building Agent App $Version for $Server"
dotnet publish src\CallCenter.AgentApp\CallCenter.AgentApp.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Only the address changes, by editing the one line, so the rest of the file
# keeps its layout and comments. Written without a byte-order mark.
$settings = Join-Path $out "appsettings.json"
$json = [IO.File]::ReadAllText($settings)
$pattern = '("BaseUrl"\s*:\s*")[^"]*(")'
if ($json -notmatch $pattern) { throw "Server:BaseUrl not found in $settings" }
$json = [regex]::Replace($json, $pattern, "`${1}$Server`${2}", 1)
[IO.File]::WriteAllText($settings, $json, (New-Object Text.UTF8Encoding $false))

Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

$size = "{0:N0} MB" -f ((Get-Item $zip).Length / 1MB)
Write-Host "Server address: $Server"
Write-Host "Folder:         $out"
Write-Host "Zip:            $zip ($size)"
