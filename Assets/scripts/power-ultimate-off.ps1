powercfg /restoredefaultschemes
if ($LASTEXITCODE -ne 0) { throw "powercfg exited with $LASTEXITCODE" }
Write-Host 'Power plans are back to the Windows defaults (Balanced).'
