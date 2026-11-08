$pf = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
$dir = Join-Path $pf 'LogiDownloadAssistant'
if (Test-Path $dir) { icacls $dir /remove:d '*S-1-1-0' | Out-Null }
