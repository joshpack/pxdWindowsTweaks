$drive = $env:SystemDrive + '\'
try { Enable-ComputerRestore -Drive $drive -ErrorAction Stop } catch { Write-Host "System Restore was already on, or couldn't be turned on: $($_.Exception.Message)" }
Checkpoint-Computer -Description 'pxd Windows Tweaks' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop
Write-Host 'Restore point created.'
