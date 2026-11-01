param([Parameter(Mandatory=$true)][string]$PythonSource, [Parameter(Mandatory=$true)][string]$DependencySource)
$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'ArtRuntime'
if (Test-Path -LiteralPath $output) { throw 'ArtRuntime already exists. Inspect it before rebuilding.' }
New-Item -ItemType Directory -Path $output | Out-Null
foreach ($name in @('python.exe','python3.dll','python312.dll','vcruntime140.dll','vcruntime140_1.dll','LICENSE.txt')) {
    Copy-Item -LiteralPath (Join-Path $PythonSource $name) -Destination (Join-Path $output $name)
}
$roots = @(@{Source=(Join-Path $PythonSource 'Lib'); Destination=(Join-Path $output 'Lib'); Standard=$true},
    @{Source=(Join-Path $PythonSource 'DLLs'); Destination=(Join-Path $output 'DLLs'); Standard=$false},
    @{Source=$DependencySource; Destination=(Join-Path $output 'Lib\site-packages'); Standard=$false})
$count = 0
foreach ($root in $roots) {
    foreach ($file in Get-ChildItem -LiteralPath $root.Source -Recurse -File) {
        $relative = $file.FullName.Substring($root.Source.Length + 1)
        if ($relative -match '(^|\\)(__pycache__|test|tests)(\\|$)' -or $file.Extension -eq '.pyc') { continue }
        if ($root.Standard -and $relative -match '^(site-packages|ensurepip|idlelib|tkinter)(\\|$)') { continue }
        $target = Join-Path $root.Destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
        $count++
    }
}
& (Join-Path $output 'python.exe') -I -X utf8 -c 'import UnityPy; from PIL import Image; print("ArtRuntime OK")'
if ($LASTEXITCODE -ne 0) { throw 'ArtRuntime import verification failed.' }
Write-Output "Copied $count runtime/library files. No game resources included."
