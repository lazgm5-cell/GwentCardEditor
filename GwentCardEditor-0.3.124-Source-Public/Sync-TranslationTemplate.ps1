param(
    [string]$UiTextPath = (Join-Path $PSScriptRoot 'Desktop\UiText.cs'),
    [string]$TemplatePath = (Join-Path $PSScriptRoot 'Desktop\Translations\_template.en-us.json')
)

$ErrorActionPreference = 'Stop'
$template = Get-Content -LiteralPath $TemplatePath -Raw | ConvertFrom-Json -AsHashtable
$added = 0
$inCategories = $false
foreach ($line in Get-Content -LiteralPath $UiTextPath) {
    if ($line -match 'CategoryRows\s*=\s*"""') { $inCategories = $true; continue }
    if ($inCategories -and $line.Trim() -eq '""";') { $inCategories = $false; continue }
    if ($line -notmatch '^([^|]+)\|([^|]*)\|([^|]*)\|([^|]*)$') { continue }
    $key = if ($inCategories) { 'category/' + $Matches[1] } else { $Matches[1] }
    if ($inCategories) { $template.Remove($Matches[1]) }
    if (!$template.ContainsKey($key)) {
        $template[$key] = $Matches[3]
        $added++
    }
}
$json = ConvertTo-Json -InputObject $template -Depth 20
[System.IO.File]::WriteAllText($TemplatePath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Output "Added $added English template entries; total: $($template.Count)."
