Write-Host 'Installing Sysinternals Autologon...'
winget install --id Microsoft.Sysinternals.Autologon --source winget --accept-source-agreements --accept-package-agreements --silent
$exe = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Microsoft.Sysinternals.Autologon*" -Recurse -Filter 'Autologon64.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) { $exe = Get-Command Autologon64.exe, Autologon.exe -ErrorAction SilentlyContinue | Select-Object -First 1 }
if ($exe) { Start-Process ($exe.FullName, $exe.Source | Where-Object { $_ } | Select-Object -First 1) -ArgumentList '/accepteula' } else { throw "Autologon was installed but couldn't be found; start it from the Start menu." }
