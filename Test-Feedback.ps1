$ErrorActionPreference = 'Stop'
$keys = [Collections.Generic.HashSet[string]]::new()
foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot 'src') -Recurse -Filter '*.cs') {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), '"((?:diag|runtime|changed|restore|missing_core|dilution)_[a-z_]+)"')) {
        if (!$match.Groups[1].Value.EndsWith('_')) { [void]$keys.Add($match.Groups[1].Value) }
    }
}
foreach ($core in @('blender','heating_plate','evaporator','chem_finisher')) { [void]$keys.Add('missing_core_'+$core) }
$checks = 0
foreach ($language in @('zh','en')) {
    $entries = @{}
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot "package/Localization/$language.csv"))) {
        $separator = $line.IndexOf(',')
        if ($separator -lt 0) { continue }
        $key = $line.Substring(0,$separator)
        if ($entries.ContainsKey($key)) { throw "Duplicate $language localization: $key" }
        $entries[$key] = $line.Substring($separator+1)
    }
    foreach ($key in $keys) {
        if (!$entries.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($entries[$key])) { throw "Missing $language feedback: $key" }
        # All dynamic numeric and named-reagent messages must format safely.
        [void][string]::Format([Globalization.CultureInfo]::InvariantCulture, $entries[$key], [object[]]@(1.25,2.5,3.75,4.5,5.5,6.5))
        $checks++
    }
}
"Feedback localization: $checks bilingual key/format checks passed"
