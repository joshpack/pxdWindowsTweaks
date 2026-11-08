Write-Host 'Running Disk Cleanup...'
Start-Process cleanmgr.exe -ArgumentList "/d $($env:SystemDrive.TrimEnd(':')) /VERYLOWDISK" -Wait
Write-Host 'Cleaning up superseded update components (this can take a while)...'
Dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase
