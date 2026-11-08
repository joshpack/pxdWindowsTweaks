$app = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application'
$setup = Get-ChildItem -Path (Join-Path $app '*\Installer\setup.exe') -ErrorAction SilentlyContinue | Select-Object -Last 1
if (-not $setup) { Write-Host 'Microsoft Edge is not installed.'; return }
# Edge's uninstaller refuses unless this placeholder exists.
New-Item -Path "$env:SystemRoot\SystemApps\Microsoft.MicrosoftEdge_8wekyb3d8bbwe\MicrosoftEdge.exe" -ItemType File -Force | Out-Null
Start-Process $setup.FullName -ArgumentList '--uninstall --system-level --force-uninstall --delete-profile' -Wait
Write-Host 'Microsoft Edge was removed.'
