$hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$text = [IO.File]::ReadAllText($hosts)
if ($text -match '# PAX Windows Tweaks: Adobe block start') { Write-Host 'The Adobe block list is already in the hosts file.'; return }
$list = Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/Ruddernation-Designs/Adobe-URL-Block-List/master/hosts' -UseBasicParsing
$lines = ($list -split "`r?`n") | Where-Object { $_ -match '^\s*0\.0\.0\.0\s+\S+' }
if ($lines.Count -lt 10) { throw 'The downloaded list looks wrong; nothing was changed.' }
Copy-Item $hosts "$hosts.pax-backup" -Force
$block = "`r`n# PAX Windows Tweaks: Adobe block start`r`n" + ($lines -join "`r`n") + "`r`n# PAX Windows Tweaks: Adobe block end`r`n"
[IO.File]::AppendAllText($hosts, $block)
ipconfig /flushdns | Out-Null
Write-Host "Added $($lines.Count) Adobe hosts (backup: hosts.pax-backup)."
