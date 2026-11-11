param([string]$OutputDirectory = 'build-output')
$ErrorActionPreference = 'Stop'

$output = Join-Path $PSScriptRoot $OutputDirectory
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
New-Item -ItemType Directory -Path $output | Out-Null

foreach ($project in @(
    @{ Path='Tests\CardEditor.Tests.csproj'; Runtime=$false },
    @{ Path='Desktop\CardEditor.Desktop.csproj'; Runtime=$true },
    @{ Path='AudioDecode\AudioDecode.csproj'; Runtime=$true }
)) {
    $restoreArgs = @('restore', (Join-Path $PSScriptRoot $project.Path), '--configfile', (Join-Path $PSScriptRoot 'NuGet.Config'))
    if ($project.Runtime) { $restoreArgs += @('-r','win-x64') }
    & dotnet @restoreArgs
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore failed for $($project.Path)." }
}
$testLog = Join-Path $output 'tests.log'
dotnet run --project (Join-Path $PSScriptRoot 'Tests\CardEditor.Tests.csproj') -c Release --no-restore *> $testLog
if ($LASTEXITCODE -ne 0) { throw 'The test suite failed.' }
Write-Output "Test suite passed. Details: $testLog"

dotnet publish (Join-Path $PSScriptRoot 'Desktop\CardEditor.Desktop.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $output 'Editor') --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Editor publish failed.' }
dotnet publish (Join-Path $PSScriptRoot 'AudioDecode\AudioDecode.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $output 'Editor\AudioDecode') --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Audio decoder publish failed.' }

$tools = Join-Path $output 'Editor\ArtTools'
New-Item -ItemType Directory -Path $tools | Out-Null
foreach ($name in @('art_swap.py','cardback_swap.py','global_visuals.py','audio_swap.py','custom_audio.py','media_smoke.py','fiery_frames.py')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ArtTools\$name") -Destination $tools
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AudioPlugin\NAudio-LICENSE.txt') -Destination (Join-Path $output 'Editor\AudioDecode\NAudio-LICENSE.txt')
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ArtRuntime')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ArtRuntime') -Destination (Join-Path $output 'Editor') -Recurse
} else {
    Write-Warning 'ArtRuntime is not built. Card art import will be unavailable until it is generated; see BUILDING.md.'
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README') -Destination (Join-Path $output 'Editor') -Recurse
Write-Output "Build complete: $output\Editor\GwentCardEditor.exe"
