$ErrorActionPreference = 'Stop'
if (Get-Process -Name Gwent -ErrorAction SilentlyContinue) { throw 'Close GWENT before testing the installer guards.' }
$root = Join-Path $env:TEMP ('FieryInstallerTest-' + [guid]::NewGuid().ToString('N'))
$client = Join-Path $root 'client'
$manager = Join-Path $PSScriptRoot 'Manage-FieryFiend.ps1'
$checks = 0
function Check([bool]$value, [string]$name) { if (!$value) { throw $name }; $script:checks++ }
function Snapshot([string]$path) {
    @(Get-ChildItem -LiteralPath $path -File -Recurse | ForEach-Object { $_.FullName.Substring($path.Length) + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash } | Sort-Object) -join '|'
}
try {
    foreach ($file in @('Gwent.exe','Gwent_Data\StreamingAssets\data_definitions','Gwent_Data\StreamingAssets\global','BepInEx\core\BepInEx.Unity.IL2CPP.dll','BepInEx\interop\Assembly-CSharp.dll')) {
        $path = Join-Path $client $file; New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        [IO.File]::WriteAllBytes($path, [Text.Encoding]::ASCII.GetBytes('fake test marker; never executed'))
    }
    $before = Snapshot $client
    & $manager -Action Install -ClientPath $client
    $plugin = Join-Path $client 'BepInEx\plugins\GwentCardEditor.FieryFiend.Experimental'
    Check (Test-Path -LiteralPath (Join-Path $plugin 'GwentCardEditor.FieryFiend.Experimental.dll')) 'Plugin not installed'
    $installed = Snapshot $client
    $rejected = $false
    try { & $manager -Action Install -ClientPath $client } catch { $rejected = $true }
    Check $rejected 'Existing plugin overwritten'
    Check ((Snapshot $client) -eq $installed) 'Failed reinstallation changed files'
    $config = Join-Path $plugin 'config.json'; $original = [IO.File]::ReadAllBytes($config)
    [IO.File]::WriteAllText($config, 'foreign configuration')
    $changed = Snapshot $client; $rejected = $false
    try { & $manager -Action Remove -ClientPath $client } catch { $rejected = $true }
    Check $rejected 'Foreign edit not protected'
    Check ((Snapshot $client) -eq $changed) 'Failed removal changed files'
    [IO.File]::WriteAllBytes($config, $original)
    & $manager -Action Remove -ClientPath $client
    Check ((Snapshot $client) -eq $before) 'Removal did not restore original client file set'
    & $manager -Action Remove -ClientPath $client
    Check ((Snapshot $client) -eq $before) 'Repeated removal changed files'
    & $manager -Action Install -ClientPath $client
    [IO.File]::WriteAllText((Join-Path $plugin 'keep.txt'), 'foreign file')
    & $manager -Action Remove -ClientPath $client
    Check ((Get-Content -LiteralPath (Join-Path $plugin 'keep.txt') -Raw) -eq 'foreign file') 'Foreign file removed'
    $unsupported = Join-Path $root 'unsupported'; New-Item -ItemType Directory -Path $unsupported | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $unsupported 'Gwent.exe'), @())
    $state = Snapshot $unsupported; $rejected = $false
    try { & $manager -Action Install -ClientPath $unsupported } catch { $rejected = $true }
    Check $rejected 'Missing loader not rejected'
    Check ((Snapshot $unsupported) -eq $state) 'Unsupported client changed'
    [pscustomobject]@{ Passed=$true; Checks=$checks; InstalledIntoGame=$false }
} finally {
    $absolute = [IO.Path]::GetFullPath($root)
    if (!$absolute.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\FieryInstallerTest-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
