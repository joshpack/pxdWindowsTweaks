$hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$text = [IO.File]::ReadAllText($hosts)
# Migration: versions before the pxd rename marked the block with the app's former name (PAX), so both markers count.
$new = [regex]::Replace($text, '(?s)\r?\n# (?:pxd|PAX) Windows Tweaks: Adobe block start.*?# (?:pxd|PAX) Windows Tweaks: Adobe block end\r?\n', '')
if ($new -eq $text) { Write-Host 'No pxd Adobe block found in the hosts file.'; return }
[IO.File]::WriteAllText($hosts, $new)
ipconfig /flushdns | Out-Null
Write-Host 'Removed the Adobe block list.'
