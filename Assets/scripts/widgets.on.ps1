Get-Process -Name '*Widget*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
foreach ($p in 'Microsoft.WidgetsPlatformRuntime', 'MicrosoftWindows.Client.WebExperience') {
    $pkgs = Get-AppxPackage -Name $p -AllUsers -ErrorAction SilentlyContinue
    if (-not $pkgs) { Write-Host "$p is not installed."; continue }
    $pkgs | Remove-AppxPackage -AllUsers -ErrorAction Continue
    Write-Host "Removed $p"
}
