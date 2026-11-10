Write-Host 'Stopping the update services...'
foreach ($s in 'BITS', 'wuauserv', 'appidsvc', 'cryptsvc') { Stop-Service -Name $s -Force -ErrorAction SilentlyContinue }
Remove-Item "$env:ProgramData\Microsoft\Network\Downloader\qmgr*.dat" -Force -ErrorAction SilentlyContinue
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($d in "$env:SystemRoot\SoftwareDistribution", "$env:SystemRoot\System32\catroot2") {
    if (Test-Path $d) {
        try { Rename-Item $d "$(Split-Path $d -Leaf).pxd-$stamp" -ErrorAction Stop; Write-Host "Renamed $d (a fresh one is made)" }
        catch { Write-Host "Couldn't rename $d : $($_.Exception.Message)" }
    }
}
Write-Host 'Re-registering the update components...'
foreach ($dll in 'atl.dll','urlmon.dll','mshtml.dll','jscript.dll','vbscript.dll','scrrun.dll','msxml3.dll','msxml6.dll','actxprxy.dll','softpub.dll','wintrust.dll','dssenh.dll','rsaenh.dll','cryptdlg.dll','oleaut32.dll','ole32.dll','shell32.dll','wuapi.dll','wuaueng.dll','wups.dll','wups2.dll','qmgr.dll','qmgrprxy.dll') {
    Start-Process regsvr32.exe -ArgumentList '/s', $dll -Wait -WindowStyle Hidden
}
foreach ($n in 'AccountDomainSid', 'PingID', 'SusClientId') { Remove-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate' -Name $n -ErrorAction SilentlyContinue }
Write-Host 'Resetting WinHTTP proxy and Winsock...'
netsh winhttp reset proxy | Out-Null
netsh winsock reset | Out-Null
Get-BitsTransfer -AllUsers -ErrorAction SilentlyContinue | Remove-BitsTransfer -ErrorAction SilentlyContinue
Write-Host 'Starting the update services...'
foreach ($s in 'BITS', 'wuauserv', 'cryptsvc') { Set-Service -Name $s -StartupType Manual -ErrorAction SilentlyContinue; Start-Service -Name $s -ErrorAction SilentlyContinue }
Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\AppIDSvc' -Name Start -Value 3 -ErrorAction SilentlyContinue
Start-Service appidsvc -ErrorAction SilentlyContinue
try { (New-Object -ComObject Microsoft.Update.AutoUpdate).DetectNow(); Write-Host 'Asked Windows Update to check for updates.' } catch { }
Write-Host 'Windows Update was reset. Restart the PC before checking for updates.'
