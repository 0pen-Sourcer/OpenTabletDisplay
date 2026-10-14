. (Join-Path $PSScriptRoot 'common.ps1')
$exe = Join-Path $script:InstallRoot 'FSDL.exe'
Write-Host ('FSDL ' + $script:FSDLVersion + ' status') -ForegroundColor Cyan
Write-Host ('Install root: ' + $script:InstallRoot)

$task = Get-ScheduledTask -TaskName $script:TaskName -ErrorAction SilentlyContinue
if ($task) { Write-Host ('Agent task: ' + $task.State) } else { Write-Host 'Agent task: NOT INSTALLED' -ForegroundColor Yellow }
$svc = Get-Service -Name SunshineService -ErrorAction SilentlyContinue
if ($svc) { Write-Host ('SunshineService: ' + $svc.Status) } else { Write-Host 'SunshineService: NOT INSTALLED' -ForegroundColor Yellow }
$vdd = @(Get-FSDLVddDevices)
if ($vdd.Count -gt 0) {
    foreach ($d in $vdd) { Write-Host ('VDD: ' + $d.Status + '  ' + $d.InstanceId) }
} else { Write-Host 'VDD: NOT FOUND' -ForegroundColor Yellow }

$enrollment = Join-Path $script:InstallRoot 'enrollment.json'
if (Test-Path $enrollment) {
    try {
        $e = Get-Content -LiteralPath $enrollment -Raw | ConvertFrom-Json
        Write-Host ('Android enrollment: ' + $e.serial + '  ' + (($e.brand, $e.model) -join ' ').Trim())
    } catch { Write-Host 'Android enrollment: unreadable' -ForegroundColor Yellow }
} else { Write-Host 'Android enrollment: none yet' }

if (Test-Path $exe) {
    Write-Host ''
    & $exe doctor
}

$agentLog = Join-Path $script:InstallRoot 'runtime\agent.log'
if (Test-Path $agentLog) {
    Write-Host ''
    Write-Host 'Recent agent log:' -ForegroundColor Cyan
    Get-Content -LiteralPath $agentLog -Tail 40
}
