$dir = Join-Path $env:SystemRoot 'Installer\Razer'
if (Test-Path $dir) { Remove-Item "$dir\*" -Recurse -Force -ErrorAction SilentlyContinue } else { New-Item -Path $dir -ItemType Directory | Out-Null }
icacls $dir /deny '*S-1-1-0:(W)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls exited with $LASTEXITCODE" }
Write-Host 'Razer installer folder locked.'
