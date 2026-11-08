foreach ($dir in @($env:TEMP, (Join-Path $env:SystemRoot 'Temp'))) {
    $before = (Get-ChildItem $dir -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    Get-ChildItem $dir -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike 'PaxWindowsTweaks*' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    $after = (Get-ChildItem $dir -Recurse -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    Write-Host ("{0}: freed {1:N0} MB" -f $dir, (($before - $after) / 1MB))
}
