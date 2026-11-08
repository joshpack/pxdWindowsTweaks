powercfg.exe /hibernate on
if ($LASTEXITCODE -ne 0) { throw "powercfg exited with $LASTEXITCODE" }
Write-Host 'Hibernation is on.'
