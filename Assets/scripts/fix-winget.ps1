Write-Host 'Re-registering the App Installer (winget)...'
try {
    Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction Stop
    Write-Host 'App Installer re-registered.'
} catch {
    Write-Host "Re-registering failed ($($_.Exception.Message)); downloading the latest App Installer..."
    $tmp = Join-Path $env:TEMP 'PaxWindowsTweaks-winget.msixbundle'
    Invoke-WebRequest -Uri 'https://aka.ms/getwinget' -OutFile $tmp -UseBasicParsing
    Add-AppxPackage -Path $tmp -ForceApplicationShutdown
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}
winget source reset --force
winget --version
