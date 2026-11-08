$out = powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
if ($LASTEXITCODE -ne 0) { throw "powercfg exited with $LASTEXITCODE" }
$guid = ([regex]'[A-Fa-f0-9]{8}-[A-Fa-f0-9-]{27}').Match(($out | Out-String)).Value
if (-not $guid) { throw 'powercfg did not return a plan ID.' }
powercfg /setactive $guid
Write-Host 'The Ultimate Performance plan is added and active.'
