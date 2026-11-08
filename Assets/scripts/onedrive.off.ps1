Write-Host 'Installing OneDrive...'
winget install --id Microsoft.OneDrive --source winget --accept-source-agreements --accept-package-agreements --silent
if (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Services\OneSyncSvc') { Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\OneSyncSvc' -Name Start -Value 2 }
