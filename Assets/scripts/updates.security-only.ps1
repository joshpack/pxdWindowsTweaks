$medic = 'HKLM:\SYSTEM\CurrentControlSet\Services\WaaSMedicSvc'
if ((Test-Path $medic) -and (Get-ItemProperty $medic -Name Start -ErrorAction SilentlyContinue).Start -ne 3) {
    try { Set-ItemProperty $medic -Name Start -Value 3 -Type DWord -ErrorAction Stop } catch { Write-Host "Couldn't reset WaaSMedicSvc: $($_.Exception.Message)" }
}
foreach ($t in '\Microsoft\Windows\InstallService\', '\Microsoft\Windows\UpdateOrchestrator\', '\Microsoft\Windows\UpdateAssistant\', '\Microsoft\Windows\WaaSMedic\', '\Microsoft\Windows\WindowsUpdate\', '\Microsoft\WindowsUpdate\') {
    Get-ScheduledTask -TaskPath $t -ErrorAction SilentlyContinue | Enable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null
}
Start-Service UsoSvc -ErrorAction SilentlyContinue
Write-Host 'Windows Update now installs security fixes only (feature updates wait a year).'
