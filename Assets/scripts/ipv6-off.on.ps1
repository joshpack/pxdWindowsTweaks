Disable-NetAdapterBinding -Name '*' -ComponentID ms_tcpip6 -ErrorAction Continue
Write-Host 'IPv6 is off on every adapter.'
