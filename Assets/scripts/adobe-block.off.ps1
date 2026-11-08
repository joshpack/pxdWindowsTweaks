$hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$text = [IO.File]::ReadAllText($hosts)
$new = [regex]::Replace($text, '(?s)\r?\n# PAX Windows Tweaks: Adobe block start.*?# PAX Windows Tweaks: Adobe block end\r?\n', '')
if ($new -eq $text) { Write-Host 'No PAX Adobe block found in the hosts file.'; return }
[IO.File]::WriteAllText($hosts, $new)
ipconfig /flushdns | Out-Null
Write-Host 'Removed the Adobe block list.'
