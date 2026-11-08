Write-Host '== Checking the disk (chkdsk /scan) =='
chkdsk.exe $env:SystemDrive /scan /perf
if ($LASTEXITCODE -ge 3) { throw "chkdsk found problems it can't fix online (exit $LASTEXITCODE). Run chkdsk /f at the next restart." }
Write-Host '== Checking system files (sfc /scannow) =='
sfc.exe /scannow
Write-Host '== Repairing the Windows image (DISM /RestoreHealth) =='
DISM.exe /Online /Cleanup-Image /RestoreHealth
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) { throw "DISM exited with $LASTEXITCODE" }
Write-Host '== Checking system files again =='
sfc.exe /scannow
Write-Host 'Done. Restart the PC if anything was repaired.'
