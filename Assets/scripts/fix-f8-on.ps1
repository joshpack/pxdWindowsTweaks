bcdedit /set '{current}' bootmenupolicy Legacy
if ($LASTEXITCODE -ne 0) { throw "bcdedit exited with $LASTEXITCODE" }
Write-Host 'Press F8 while the PC starts to get the recovery menu.'
