$sourcePath = Join-Path $PSScriptRoot "Pages/User/Create.cshtml"
$outputPath = Join-Path $PSScriptRoot "_inspect_create.svg"
$lines = [System.IO.File]::ReadAllLines($sourcePath)
$patterns = @(
    'Submit Another',
    'submitAnother',
    'Concern Submitted',
    'Recommendation Submitted',
    'isSubmitting',
    'submitting',
    'disabled'
)
$selected = New-Object 'System.Collections.Generic.SortedSet[int]'
for ($i = 0; $i -lt $lines.Length; $i++) {
    foreach ($pattern in $patterns) {
        if ($lines[$i].IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $start = [Math]::Max(0, $i - 8)
            $end = [Math]::Min($lines.Length - 1, $i + 18)
            for ($j = $start; $j -le $end; $j++) { [void]$selected.Add($j) }
            break
        }
    }
}

$display = New-Object 'System.Collections.Generic.List[string]'
$previous = -2
foreach ($index in $selected) {
    if ($index -gt ($previous + 1)) { $display.Add('...') }
    $display.Add(('{0,5}: {1}' -f ($index + 1), $lines[$index]))
    $previous = $index
}

function Encode([string]$value) {
    return [System.Security.SecurityElement]::Escape($value)
}

$lineHeight = 18
$height = [Math]::Max(300, ($display.Count + 2) * $lineHeight)
$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine("<svg xmlns='http://www.w3.org/2000/svg' width='1800' height='$height'>")
[void]$builder.AppendLine("<rect width='100%' height='100%' fill='#111827'/>")
[void]$builder.AppendLine("<style>text{font-family:Consolas,monospace;font-size:13px;fill:#e5e7eb}</style>")
$y = 24
foreach ($line in $display) {
    [void]$builder.AppendLine("<text x='12' y='$y'>$(Encode $line)</text>")
    $y += $lineHeight
}
[void]$builder.AppendLine('</svg>')
[System.IO.File]::WriteAllText($outputPath, $builder.ToString())
