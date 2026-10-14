. (Join-Path $PSScriptRoot 'common.ps1')
$exe = Join-Path $script:InstallRoot 'FSDL.exe'
if (-not (Test-Path $exe)) { throw 'FSDL is not installed.' }
Stop-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
& $exe reset-device
$rc = $LASTEXITCODE
if (Get-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue) {
    Start-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
}
exit $rc
