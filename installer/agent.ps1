$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$exe = Join-Path $root 'FSDL.exe'
$runtime = Join-Path $root 'runtime'
$log = Join-Path $runtime 'agent.log'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null

function Rotate-Log {
    try {
        if ((Test-Path $log) -and ((Get-Item $log).Length -gt 2MB)) {
            $old = $log + '.1'
            Remove-Item -LiteralPath $old -Force -ErrorAction SilentlyContinue
            Move-Item -LiteralPath $log -Destination $old -Force
        }
    } catch {}
}

while ($true) {
    try {
        Rotate-Log
        Add-Content -LiteralPath $log -Value ("`n=== agent launch " + (Get-Date -Format o) + " ===") -Encoding UTF8
        if (-not (Test-Path $exe)) {
            Add-Content -LiteralPath $log -Value 'FSDL.exe is missing; waiting for repair/update.' -Encoding UTF8
            Start-Sleep -Seconds 10
            continue
        }
        & $exe daemon *>> $log
        $rc = $LASTEXITCODE
        Add-Content -LiteralPath $log -Value ('daemon exited rc=' + $rc + ' at ' + (Get-Date -Format o)) -Encoding UTF8
    } catch {
        Add-Content -LiteralPath $log -Value ('agent exception: ' + $_.Exception.Message) -Encoding UTF8
    }
    Start-Sleep -Seconds 5
}
