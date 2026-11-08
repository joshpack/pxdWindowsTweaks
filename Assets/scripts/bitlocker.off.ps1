Enable-BitLocker -MountPoint $env:SystemDrive -EncryptionMethod XtsAes128 -UsedSpaceOnly -TpmProtector -ErrorAction Stop | Out-Null
Write-Host "Encryption of $env:SystemDrive has started. Save your recovery key from Settings > Privacy & security > Device encryption."
