bcdedit /set '{current}' bootmenupolicy Standard
if ($LASTEXITCODE -ne 0) { throw "bcdedit exited with $LASTEXITCODE" }
Write-Host 'F8 boot menu is off again.'
