$db = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalState\store.db'
if (-not (Test-Path -LiteralPath $db)) { Write-Host 'Nothing to undo.'; return }
icacls $db /remove:d '*S-1-1-0' | Out-Null
Write-Host 'Store suggestions are allowed in Start search again.'
