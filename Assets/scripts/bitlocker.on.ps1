$v = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop
if ($v.VolumeStatus -eq 'FullyDecrypted') { Write-Host "BitLocker is already off on $env:SystemDrive."; return }
Disable-BitLocker -MountPoint $env:SystemDrive -ErrorAction Stop | Out-Null
Write-Host "Decryption of $env:SystemDrive has started; it carries on in the background."
