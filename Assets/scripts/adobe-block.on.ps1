$hosts = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$text = [IO.File]::ReadAllText($hosts)
# Migration: versions before the pxd rename marked the block with the app's former name ('PAX Windows Tweaks').
# Such a block gets the current markers, so it counts as already there and undo keeps finding it.
$former = '# PAX Windows Tweaks: Adobe block '
if ($text.Contains($former)) {
    $text = $text.Replace($former, '# pxd Windows Tweaks: Adobe block ')
    [IO.File]::WriteAllText($hosts, $text)
}
if ($text -match '# pxd Windows Tweaks: Adobe block start') { Write-Host 'The Adobe block list is already in the hosts file.'; return }
$list = Invoke-RestMethod -Uri 'https://raw.githubusercontent.com/Ruddernation-Designs/Adobe-URL-Block-List/master/hosts' -UseBasicParsing
$lines = ($list -split "`r?`n") | Where-Object { $_ -match '^\s*0\.0\.0\.0\s+\S+' }
if ($lines.Count -lt 10) { throw 'The downloaded list looks wrong; nothing was changed.' }
Copy-Item $hosts "$hosts.pxd-backup" -Force
$block = "`r`n# pxd Windows Tweaks: Adobe block start`r`n" + ($lines -join "`r`n") + "`r`n# pxd Windows Tweaks: Adobe block end`r`n"
[IO.File]::AppendAllText($hosts, $block)
ipconfig /flushdns | Out-Null
Write-Host "Added $($lines.Count) Adobe hosts (backup: hosts.pxd-backup)."
