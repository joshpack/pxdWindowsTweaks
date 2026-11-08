$folder = $env:OneDrive
$hasFolder = $folder -and (Test-Path -LiteralPath $folder -PathType Container)
$acl = if ($hasFolder) { Get-Acl -LiteralPath $folder } else { $null }
try {
    if ($hasFolder) {
        # Administrators may not delete anything in the folder while the uninstaller runs.
        icacls $folder /deny '*S-1-5-32-544:(D,DC)' | Out-Null
    }
    Get-Process OneDrive -ErrorAction SilentlyContinue | Stop-Process -Force
    $setup = @("$env:SystemRoot\System32\OneDriveSetup.exe", "$env:SystemRoot\SysWOW64\OneDriveSetup.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($setup) {
        Write-Host 'Uninstalling OneDrive...'
        Start-Process $setup -ArgumentList '/uninstall' -Wait
    } else {
        winget uninstall --id Microsoft.OneDrive --silent --accept-source-agreements
    }
    Get-Process FileCoAuth -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($left in "$env:LOCALAPPDATA\Microsoft\OneDrive", "$env:ProgramData\Microsoft OneDrive") {
        if (Test-Path -LiteralPath $left) { Remove-Item -LiteralPath $left -Recurse -Force -ErrorAction SilentlyContinue; Write-Host "Removed $left" }
    }
} finally {
    if ($acl) { Set-Acl -LiteralPath $folder -AclObject $acl }
}
if ($hasFolder -and -not (Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)) {
    [IO.Directory]::Delete($folder, $false)
    [Environment]::SetEnvironmentVariable('OneDrive', $null, 'User')
    Write-Host 'Removed the empty OneDrive folder.'
} elseif ($hasFolder) {
    Write-Host "Your files are still in $folder."
}
if (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Services\OneSyncSvc') { Set-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\OneSyncSvc' -Name Start -Value 4 }
