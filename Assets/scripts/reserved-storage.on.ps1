DISM.exe /Online /Set-ReservedStorageState /State:Disabled
if ($LASTEXITCODE -ne 0) { throw "DISM exited with $LASTEXITCODE (it can't change this while an update is pending)" }
