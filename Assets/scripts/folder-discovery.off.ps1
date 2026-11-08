$shell = 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell'
foreach ($k in 'Bags', 'BagMRU') {
    if (Test-Path "$shell\$k") { Remove-Item "$shell\$k" -Recurse -Force -ErrorAction SilentlyContinue; Write-Host "Cleared $k" }
}
Write-Host 'Explorer detects folder types again. Sign out and back in to see it.'
