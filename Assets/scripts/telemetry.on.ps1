try { Set-MpPreference -SubmitSamplesConsent 2 -ErrorAction Stop; Write-Host 'Defender no longer uploads samples.' }
catch { Write-Host "Defender sample setting left alone ($($_.Exception.Message))." }
[Environment]::SetEnvironmentVariable('POWERSHELL_TELEMETRY_OPTOUT', '1', 'Machine')
Write-Host 'PowerShell 7 telemetry is off.'
