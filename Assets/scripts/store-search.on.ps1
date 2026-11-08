$db = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db'
if (-not (Test-Path -LiteralPath $db)) { Write-Host 'The Store has no search database yet (open the Store once, then run this again).'; return }
icacls $db /deny '*S-1-1-0:F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls exited with $LASTEXITCODE" }
Write-Host 'Store suggestions are blocked in Start search.'
