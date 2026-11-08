Start-Service w32time -ErrorAction SilentlyContinue
w32tm /config /update /manualpeerlist:"pool.ntp.org,0x8" /syncfromflags:MANUAL
Restart-Service w32time
w32tm /resync
Write-Host 'The clock now syncs with pool.ntp.org.'
