try { Set-MpPreference -SubmitSamplesConsent 1 -ErrorAction Stop } catch { Write-Host "Defender sample setting left alone ($($_.Exception.Message))." }
[Environment]::SetEnvironmentVariable('POWERSHELL_TELEMETRY_OPTOUT', $null, 'Machine')
