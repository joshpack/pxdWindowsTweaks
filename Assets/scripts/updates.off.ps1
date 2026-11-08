foreach ($s in 'BITS', 'wuauserv', 'UsoSvc') { Stop-Service -Name $s -Force -ErrorAction SilentlyContinue }
foreach ($t in '\Microsoft\Windows\InstallService\', '\Microsoft\Windows\UpdateOrchestrator\', '\Microsoft\Windows\UpdateAssistant\', '\Microsoft\Windows\WaaSMedic\', '\Microsoft\Windows\WindowsUpdate\', '\Microsoft\WindowsUpdate\') {
    Get-ScheduledTask -TaskPath $t -ErrorAction SilentlyContinue | Disable-ScheduledTask -ErrorAction SilentlyContinue | Out-Null
}
Remove-Item -Path "$env:SystemRoot\SoftwareDistribution\Download\*" -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Windows Update is off. Choose Default to turn it back on.'
