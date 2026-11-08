Get-AppxPackage -AllUsers -Name '*Copilot*' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Removing $($_.Name)"; $_ | Remove-AppxPackage -AllUsers -ErrorAction Continue }
Get-AppxPackage -AllUsers -Name 'Microsoft.MicrosoftOfficeHub' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Removing $($_.Name)"; $_ | Remove-AppxPackage -AllUsers -ErrorAction Continue }
$core = Get-AppxPackage -Name 'MicrosoftWindows.Client.CoreAI' -ErrorAction SilentlyContinue
if ($core) {
    # A system app can't be removed until it is marked end-of-life for this user.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    New-Item "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\EndOfLife\$sid\$($core.PackageFullName)" -Force | Out-Null
    Remove-AppxPackage $core.PackageFullName -ErrorAction Continue
    Write-Host 'Removed the Windows AI core package.'
}
if (Get-Service -Name WSAIFabricSvc -ErrorAction SilentlyContinue) { Set-Service -Name WSAIFabricSvc -StartupType Disabled; Write-Host 'AI fabric service disabled.' }
$recall = Get-WindowsOptionalFeature -Online -FeatureName Recall -ErrorAction SilentlyContinue
if ($recall -and $recall.State -eq 'Enabled') { Disable-WindowsOptionalFeature -Online -FeatureName Recall -NoRestart | Out-Null; Write-Host 'Recall is off.' }
Write-Host 'Windows AI is off.'
