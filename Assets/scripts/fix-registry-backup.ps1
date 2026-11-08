# Windows stopped filling RegBack in 2018 unless this is set.
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Configuration Manager' -Name EnablePeriodicBackup -Value 1 -Type DWord
$trigger = New-ScheduledTaskTrigger -Daily -At '00:30'
$backup = New-ScheduledTaskAction -Execute 'schtasks.exe' -Argument '/run /i /tn "\Microsoft\Windows\Registry\RegIdleBackup"'
Register-ScheduledTask -TaskName 'PAX Registry Backup' -TaskPath '\PAX\' -Action $backup -Trigger $trigger -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
Write-Host 'The registry is backed up to C:\Windows\System32\config\RegBack every night at 00:30.'
