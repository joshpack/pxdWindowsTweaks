netsh winsock reset
netsh int ip reset
ipconfig /flushdns
Write-Host 'Network settings were reset. Restart the PC to finish.'
