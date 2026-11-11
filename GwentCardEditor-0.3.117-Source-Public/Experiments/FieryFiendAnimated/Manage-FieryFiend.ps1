param(
    [ValidateSet('Install','Remove','Status')][string]$Action = 'Status',
    [Parameter(Mandatory=$true)][string]$ClientPath
)
$ErrorActionPreference = 'Stop'
$client = (Resolve-Path -LiteralPath $ClientPath).Path
if (!(Test-Path -LiteralPath (Join-Path $client 'Gwent.exe'))) { throw 'Select the client folder containing Gwent.exe.' }
$source = Join-Path $PSScriptRoot 'Package\BepInEx\plugins\GwentCardEditor.FieryFiend.Experimental'
$target = [IO.Path]::GetFullPath((Join-Path $client 'BepInEx\plugins\GwentCardEditor.FieryFiend.Experimental'))
if (!$target.StartsWith($client.TrimEnd('\') + '\BepInEx\plugins\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe target.' }
foreach ($directory in @((Join-Path $client 'BepInEx'), (Join-Path $client 'BepInEx\plugins'), $target)) {
    if ((Test-Path -LiteralPath $directory) -and ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked plugin directories are not supported by this experimental installer.' }
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-files.json') -Raw | ConvertFrom-Json
if ($manifest.Version -ne 1 -or $manifest.Files.Count -ne 13) { throw 'Invalid package manifest.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($record in $manifest.Files) {
    if (!$seen.Add($record.Path) -or $record.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Duplicate path or invalid hash.' }
    if ($record.Path -notmatch '^(GwentCardEditor\.FieryFiend\.Experimental\.dll|config\.json|assets/scene\.json|assets/textures/-?[0-9]+\.png)$') { throw 'Unsafe package path.' }
    if ((Get-FileHash -LiteralPath (Join-Path $source $record.Path)).Hash -ne $record.Sha256) { throw ('Package checksum mismatch: ' + $record.Path) }
}
if ($Action -eq 'Status') { [pscustomobject]@{ Client=$client; Installed=(Test-Path -LiteralPath (Join-Path $target 'GwentCardEditor.FieryFiend.Experimental.dll')); Target=$target }; return }
if (Get-Process -Name Gwent -ErrorAction SilentlyContinue) { throw 'Close all GWENT clients first.' }
if ($Action -eq 'Install') {
    foreach ($required in @('BepInEx\core\BepInEx.Unity.IL2CPP.dll','BepInEx\interop\Assembly-CSharp.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $client $required))) { throw 'Existing BepInEx IL2CPP is required; this package does not install it.' }
    }
    if (Test-Path -LiteralPath $target) { throw 'Plugin folder already exists. Remove the previous test package first; nothing was overwritten.' }
    $stage = $target + '.install-' + [guid]::NewGuid().ToString('N')
    if (!([IO.Path]::GetFullPath($stage)).StartsWith($client.TrimEnd('\') + '\BepInEx\plugins\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
    try {
        foreach ($record in $manifest.Files) {
            $dest = Join-Path $stage $record.Path
            New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $source $record.Path) -Destination $dest
            if ((Get-FileHash -LiteralPath $dest).Hash -ne $record.Sha256) { throw 'Installed checksum mismatch.' }
        }
        if (Get-Process -Name Gwent -ErrorAction SilentlyContinue) { throw 'GWENT started during installation. Installation cancelled.' }
        Move-Item -LiteralPath $stage -Destination $target
    } finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
    'Experimental Fiery Fiend installed for card 202103 only. No game data or global bundle changed.'
} else {
    if (!(Test-Path -LiteralPath $target)) { 'Already removed.'; return }
    foreach ($record in $manifest.Files) {
        $dest = Join-Path $target $record.Path
        if (!(Test-Path -LiteralPath $dest) -or (Get-FileHash -LiteralPath $dest).Hash -ne $record.Sha256) { throw ('Modified/missing file; removal cancelled without writes: ' + $record.Path) }
    }
    $removed = [Collections.Generic.List[object]]::new()
    try {
        foreach ($record in $manifest.Files) { Remove-Item -LiteralPath (Join-Path $target $record.Path); $removed.Add($record) }
    } catch {
        foreach ($record in $removed) {
            $dest = Join-Path $target $record.Path
            if (!(Test-Path -LiteralPath $dest)) { Copy-Item -LiteralPath (Join-Path $source $record.Path) -Destination $dest }
        }
        throw
    }
    foreach ($directory in Get-ChildItem -LiteralPath $target -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending) {
        if (!(Get-ChildItem -LiteralPath $directory.FullName -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $directory.FullName }
    }
    if (!(Get-ChildItem -LiteralPath $target -Force | Select-Object -First 1)) { Remove-Item -LiteralPath $target }
    'Experimental plugin removed. Diagnostic frame PNGs, if any, were preserved; other files were not deleted.'
}
