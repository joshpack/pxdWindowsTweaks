$shell = 'HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell'
foreach ($k in 'Bags', 'BagMRU') {
    if (Test-Path "$shell\$k") { Remove-Item "$shell\$k" -Recurse -Force -ErrorAction SilentlyContinue; Write-Host "Cleared $k" }
}
$all = "$shell\Bags\AllFolders\Shell"
New-Item -Path $all -Force | Out-Null
New-ItemProperty -Path $all -Name FolderType -Value NotSpecified -PropertyType String -Force | Out-Null
Write-Host 'Every folder now opens with the General items layout. Sign out and back in to see it.'
