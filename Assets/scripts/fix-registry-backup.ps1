# Windows stopped filling RegBack in 2018 unless this is set.
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager' -Name EnablePeriodicBackup -Value 1 -Type DWord
$trigger = New-ScheduledTaskTrigger -Daily -At '00:30'
$backup = New-ScheduledTaskAction -Execute 'schtasks.exe' -Argument '/run /i /tn "\Microsoft\Windows\Registry\RegIdleBackup"'
Register-ScheduledTask -TaskName 'pxd Registry Backup' -TaskPath '\pxd\' -Action $backup -Trigger $trigger -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
# Migration: versions before the pxd rename made this task under the app's former name; the new one replaces it.
Unregister-ScheduledTask -TaskPath '\PAX\' -TaskName 'PAX Registry Backup' -Confirm:$false -ErrorAction SilentlyContinue
try { $f = (New-Object -ComObject Schedule.Service); $f.Connect(); $p = $f.GetFolder('\PAX'); if ($p.GetTasks(1).Count -eq 0 -and $p.GetFolders(0).Count -eq 0) { $f.GetFolder('\').DeleteFolder('PAX', 0) } } catch { }
Write-Host 'The registry is backed up to C:\Windows\System32\config\RegBack every night at 00:30.'
