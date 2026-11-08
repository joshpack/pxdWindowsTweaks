DISM.exe /Online /Set-ReservedStorageState /State:Enabled
if ($LASTEXITCODE -ne 0) { throw "DISM exited with $LASTEXITCODE" }
