powercfg.exe /hibernate off
if ($LASTEXITCODE -ne 0) { throw "powercfg exited with $LASTEXITCODE" }
Write-Host 'Hibernation is off and hiberfil.sys was removed.'
