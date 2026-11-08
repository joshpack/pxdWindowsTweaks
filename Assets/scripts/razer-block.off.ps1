$dir = Join-Path $env:SystemRoot 'Installer\Razer'
if (Test-Path $dir) { icacls $dir /remove:d '*S-1-1-0' | Out-Null }
