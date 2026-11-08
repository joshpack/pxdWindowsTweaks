$kb = [int64]((Get-CimInstance Win32_PhysicalMemory | Measure-Object Capacity -Sum).Sum / 1KB)
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control' -Name SvcHostSplitThresholdInKB -Value $kb -Type DWord
Write-Host ("Threshold set to {0:N0} KB. Restart to group the services." -f $kb)
