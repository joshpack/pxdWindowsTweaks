Stop-Process -Name logi_download_assistant -Force -ErrorAction SilentlyContinue
$pf = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$dir = Join-Path $pf 'LogiDownloadAssistant'
if (Test-Path $dir) { Remove-Item "$dir\*" -Recurse -Force -ErrorAction SilentlyContinue } else { New-Item -Path $dir -ItemType Directory | Out-Null }
icacls $dir /deny '*S-1-1-0:(W)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw "icacls exited with $LASTEXITCODE" }
Write-Host 'Logitech Download Assistant blocked.'
