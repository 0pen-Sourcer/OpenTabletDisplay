param(
    [switch]$Elevated,
    [switch]$RemoveDependencies
)

. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-FSDLAdmin)) {
    $args = @('-Elevated')
    if ($RemoveDependencies) { $args += '-RemoveDependencies' }
    exit (Invoke-FSDLElevatedSelf $PSCommandPath $args)
}

$logPath = Join-Path $script:InstallRoot 'logs\uninstall.log'
Write-FSDLLog 'Starting OpenTabletDisplay uninstall.' $logPath

$state = $null
if (Test-Path $script:InstallStatePath) {
    try { $state = Get-Content -LiteralPath $script:InstallStatePath -Raw | ConvertFrom-Json } catch {}
}

foreach ($name in @($script:TaskName, 'FSDL USB Bridge Agent')) {
    Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
}

$exe = Join-Path $script:InstallRoot 'FSDL.exe'
if (Test-Path $exe) {
    try { & $exe cleanup | Out-Host } catch {}
}

# Restore only keys that still equal FSDL's applied value. User edits made after
# installation win over FSDL's old snapshot and are left untouched.
if ($state -and $state.sunshine_config_path -and $state.sunshine_changes) {
    $cfg = [string]$state.sunshine_config_path
    if (Test-Path $cfg) {
        $lines = @(Get-Content -LiteralPath $cfg)
        $restored = 0
        $skipped = 0
        foreach ($change in @($state.sunshine_changes)) {
            $key = [string]$change.key
            $cur = Get-FSDLConfigEntry $lines $key
            $appliedPresent = [bool]$change.applied_present
            $appliedValue = if ($null -eq $change.applied_value) { $null } else { [string]$change.applied_value }
            if (Test-FSDLConfigEntryEquals $cur $appliedPresent $appliedValue) {
                $origPresent = [bool]$change.original_present
                $origValue = if ($null -eq $change.original_value) { $null } else { [string]$change.original_value }
                $lines = Set-FSDLConfigEntry $lines $key $origPresent $origValue
                $restored++
            } else {
                $skipped++
            }
        }
        $tmp = $cfg + '.fsdl-uninstall.tmp'
        [IO.File]::WriteAllLines($tmp, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $tmp -Destination $cfg -Force
        try { Restart-FSDLSunshine } catch {}
        Write-FSDLLog ('Sunshine config restore: ' + $restored + ' keys restored, ' + $skipped + ' user-modified keys preserved.') $logPath
    }
}

if ($RemoveDependencies -and $state) {
    if ($state.vdd_installed_by_fsdl) {
        foreach ($id in @($state.vdd_instance_ids)) {
            if ($id) { & pnputil.exe /remove-device ([string]$id) | Out-Null }
        }
        foreach ($driver in @($state.vdd_driver_names)) {
            if ($driver) { & pnputil.exe /delete-driver ([string]$driver) /uninstall | Out-Null }
        }
        Write-FSDLLog 'Removed VDD instances/driver packages recorded as FSDL-owned.' $logPath
    }
    if ($state.sunshine_installed_by_fsdl) {
        $sunMsi = Join-Path $script:InstallRoot ('cache\Sunshine-' + $script:SunshineVersion + '.msi')
        if (Test-Path $sunMsi) {
            $p = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/x', ('"' + $sunMsi + '"'), '/qn', '/norestart') -Wait -PassThru
            Write-FSDLLog ('Sunshine uninstall exit code: ' + $p.ExitCode) $logPath
        } else {
            Write-FSDLLog 'Sunshine was FSDL-owned but cached MSI is missing; leaving Sunshine installed rather than guessing an uninstall product code.' $logPath
        }
    }
}

Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FSDL' -Recurse -Force -ErrorAction SilentlyContinue

# Defer deleting the directory that contains this running script.
$rootEscaped = $script:InstallRoot.Replace('"','""')
$cmd = '/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q "' + $rootEscaped + '"'
Start-Process -FilePath 'cmd.exe' -ArgumentList $cmd -WindowStyle Hidden | Out-Null
Write-Host 'OpenTabletDisplay removed. Sunshine/VDD were preserved unless -RemoveDependencies was explicitly requested and ownership was recorded.' -ForegroundColor Green
