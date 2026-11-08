Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control' -Name SvcHostSplitThresholdInKB -Value 3670016 -Type DWord
Write-Host 'Threshold back to the Windows default. Restart to split the services again.'
