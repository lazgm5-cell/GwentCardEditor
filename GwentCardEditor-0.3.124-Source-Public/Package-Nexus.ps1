param([string]$Version = '0.3.124', [string]$ReleaseRoot = (Join-Path $PSScriptRoot 'Releases'))
$ErrorActionPreference = 'Stop'
$version = $Version
$publish = Join-Path $PSScriptRoot "publish-$version"
$guides = Join-Path $PSScriptRoot 'README'
$releaseName = "GwentCardEditor-$version-Beta"
$releaseRoot = $ReleaseRoot
$release = Join-Path $releaseRoot $releaseName
$archive = Join-Path $releaseRoot "$releaseName-Windows-x64.zip"
$locales = @('en-us','ru-ru','zh-cn','pl-pl','de-de','pt-br','fr-fr','ja-jp','it-it','es-es','es-mx','zh-tw','ko-kr')
$guideLocales = @('en-us','ru-ru','zh-cn','pl-pl')

if (!(Test-Path -LiteralPath (Join-Path $publish 'GwentCardEditor.exe'))) { throw "Build the editor first: $publish" }
$runtimeConfigPath = Join-Path $publish 'GwentCardEditor.runtimeconfig.json'
if (!(Test-Path -LiteralPath $runtimeConfigPath)) { throw "The editor runtime configuration is missing: $runtimeConfigPath" }
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
if (!$runtimeConfig.runtimeOptions.includedFrameworks) { throw 'The release publish folder must be self-contained. Publish with -r win-x64 --self-contained true.' }
foreach ($required in @('ArtRuntime\python.exe','ArtTools\art_swap.py','ArtTools\global_visuals.py')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $required))) { throw "The publish folder is incomplete; required art component is missing: $required. Build with Build.ps1." }
}
$preflightIssues = [System.Collections.Generic.List[string]]::new()
foreach ($locale in $locales) {
    if ($locale -notin @('en-us','ru-ru','zh-cn')) {
        $dictionaryPath = Join-Path $PSScriptRoot "Desktop\Translations\$locale.json"
        if (!(Test-Path -LiteralPath $dictionaryPath)) {
            $preflightIssues.Add("$($locale): translation dictionary is missing.")
        } else {
            try { $dictionary = Get-Content -LiteralPath $dictionaryPath -Raw | ConvertFrom-Json -AsHashtable }
            catch { $preflightIssues.Add("$($locale): translation dictionary is invalid JSON."); $dictionary = @{} }
            if (!$dictionary.Count) { $preflightIssues.Add("$($locale): translation dictionary has no entries.") }
        }
    }
}
try { & (Join-Path $PSScriptRoot 'Audit-Translations.ps1') -FailOnMissing | Out-Null }
catch { $preflightIssues.Add("One or more locale dictionaries are incomplete or have invalid placeholders.") }
foreach ($locale in $guideLocales) {
    $guide = Join-Path $guides "README-$locale.txt"
    if (!(Test-Path -LiteralPath $guide) -or (Get-Item -LiteralPath $guide).Length -lt 1000) { $preflightIssues.Add("$($locale): beta user guide is missing or incomplete.") }
}
if ($preflightIssues.Count) { throw "Release preflight failed:`n - $($preflightIssues -join "`n - ")" }
if (Test-Path -LiteralPath $release) { throw "Release folder already exists: $release" }
if (Test-Path -LiteralPath $archive) { throw "Release archive already exists: $archive" }

New-Item -ItemType Directory -Path $release -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object Extension -ne '.pdb') {
    $relative = $file.FullName.Substring($publish.Length + 1)
    if ($relative -in @('README.md','MEDIA_CHECK.md') -or $relative -like 'RELEASE_NOTES*') { continue }
    if ($relative -eq 'Translations\_template.en-us.json') { continue }
    $destination = Join-Path $release $relative
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}
New-Item -ItemType Directory -Path (Join-Path $release 'README') -Force | Out-Null
foreach ($locale in $guideLocales) { Copy-Item -LiteralPath (Join-Path $guides "README-$locale.txt") -Destination (Join-Path $release "README\README-$locale.txt") }
$releaseNotes = Join-Path $PSScriptRoot ('RELEASE_NOTES_' + $version + '_RU.md')
if (Test-Path -LiteralPath $releaseNotes) { Copy-Item -LiteralPath $releaseNotes -Destination (Join-Path $release 'README\RELEASE_NOTES_RU.md') }
Compress-Archive -LiteralPath $release -DestinationPath $archive -CompressionLevel Optimal
Write-Output "NexusMods package created: $archive"
