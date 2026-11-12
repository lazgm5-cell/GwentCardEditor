param(
    [string]$TranslationRoot = (Join-Path $PSScriptRoot 'Desktop\Translations'),
    [string[]]$Locales = @('de-de','es-es','es-mx','fr-fr','it-it','ja-jp','ko-kr','pl-pl','pt-br','zh-tw'),
    [switch]$FailOnMissing
)

$ErrorActionPreference = 'Stop'
$templatePath = Join-Path $TranslationRoot '_template.en-us.json'
$template = Get-Content -LiteralPath $templatePath -Raw | ConvertFrom-Json -AsHashtable
$placeholders = [regex]'\{[^{}]+\}'
$results = [System.Collections.Generic.List[object]]::new()
$issues = [System.Collections.Generic.List[string]]::new()

foreach ($locale in $Locales) {
    $path = Join-Path $TranslationRoot "$locale.json"
    if (!(Test-Path -LiteralPath $path)) {
        $issues.Add("$locale`: dictionary is missing.")
        continue
    }
    try { $dictionary = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable }
    catch { $issues.Add("$locale`: dictionary is invalid JSON."); continue }

    $missing = [System.Collections.Generic.List[string]]::new()
    $placeholderIssues = [System.Collections.Generic.List[string]]::new()
    $lineBreakIssues = [System.Collections.Generic.List[string]]::new()
    $unexpected = @($dictionary.Keys | Where-Object { !$template.ContainsKey($_) })
    foreach ($key in $template.Keys) {
        if (!$dictionary.ContainsKey($key) -or [string]::IsNullOrWhiteSpace([string]$dictionary[$key])) {
            $missing.Add($key)
            continue
        }
        $expected = @($placeholders.Matches([string]$template[$key]) | ForEach-Object Value | Sort-Object)
        $actual = @($placeholders.Matches([string]$dictionary[$key]) | ForEach-Object Value | Sort-Object)
        if (($expected -join "`n") -cne ($actual -join "`n")) { $placeholderIssues.Add($key) }
        $expectedBreaks = [regex]::Matches([string]$template[$key], [regex]::Escape('\n')).Count
        $actualBreaks = [regex]::Matches([string]$dictionary[$key], [regex]::Escape('\n')).Count
        if ($expectedBreaks -ne $actualBreaks) { $lineBreakIssues.Add($key) }
    }
    $results.Add([PSCustomObject]@{
        Locale = $locale
        Translated = $template.Count - $missing.Count
        Total = $template.Count
        Missing = $missing.Count
        PlaceholderMismatches = $placeholderIssues.Count
        LineBreakMismatches = $lineBreakIssues.Count
        Unexpected = $unexpected.Count
    })
    if ($missing.Count) { $issues.Add("$locale`: $($missing.Count) strings are missing.") }
    if ($placeholderIssues.Count) { $issues.Add("$locale`: placeholder mismatch in $($placeholderIssues.Count) strings: $($placeholderIssues -join ', ')") }
    if ($lineBreakIssues.Count) { $issues.Add("$locale`: line-break marker mismatch in $($lineBreakIssues.Count) strings: $($lineBreakIssues -join ', ')") }
    if ($unexpected.Count) { $issues.Add("$locale`: $($unexpected.Count) keys are not in the English template: $($unexpected -join ', ')") }
}

$results | Format-Table -AutoSize
if ($issues.Count) {
    $issues | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    if ($FailOnMissing) { throw 'Translation audit failed: dictionaries are incomplete or placeholders differ.' }
}
