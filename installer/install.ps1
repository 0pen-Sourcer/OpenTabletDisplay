param(
    [switch]$Elevated,
    [string]$OutputDeviceId = ''
)

. (Join-Path $PSScriptRoot 'common.ps1')

if (-not (Test-FSDLAdmin)) {
    $args = @('-Elevated')
    if ($OutputDeviceId) { $args += ('-OutputDeviceId "' + $OutputDeviceId + '"') }
    exit (Invoke-FSDLElevatedSelf $PSCommandPath $args)
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw ('OpenTabletDisplay ' + $script:FSDLVersion + ' requires 64-bit Windows 10 or 11.')
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$payloadExe = Join-Path $packageRoot 'payload\FSDL.exe'
$payloadProbes = Join-Path $packageRoot 'payload\probes'
if (-not (Test-Path $payloadExe)) { throw ('Release payload is incomplete: ' + $payloadExe) }
if (-not (Test-Path $payloadProbes)) { throw ('Release payload is incomplete: ' + $payloadProbes) }
$desktopExe = Join-Path $packageRoot 'OpenTabletDisplay.exe'
if (-not (Test-Path -LiteralPath $desktopExe)) {
    throw 'Native Windows desktop executable missing. Extract the complete release ZIP, not GitHub Source code ZIP.'
}
# Checksums detect damaged archives, not trusted publisher identities.
$manifest = Join-Path $packageRoot 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Package integrity manifest missing.' }
$expected = @((Get-Content -LiteralPath $manifest) | Where-Object { $_ -match '^[0-9a-f]{64}  payload/FSDL\.exe$' })
if ($expected.Count -ne 1) { throw 'Package manifest has no unambiguous helper hash.' }
if ((Get-FileHash -LiteralPath $payloadExe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected[0].Substring(0,64)) {
    throw 'USB helper failed package integrity verification. Redownload the official archive.'
}

New-Item -ItemType Directory -Force -Path $script:InstallRoot | Out-Null
# Exclusive file handle ensures no second setup can modify drivers in parallel.
$installLockPath = Join-Path $script:InstallRoot 'install.lock'
try {
    $installLock = [System.IO.File]::Open($installLockPath, [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
} catch { throw 'Another OpenTabletDisplay install or repair is running.' }
$logPath = Join-Path $script:InstallRoot 'logs\install.log'
Initialize-FSDLInstallLog $logPath

$oldState = $null
if (Test-Path $script:InstallStatePath) {
    try { $oldState = Get-Content -LiteralPath $script:InstallStatePath -Raw | ConvertFrom-Json } catch {}
}

$installedSunshineThisRun = $false
$installedVddThisRun = $false
$configBackup = $null
$configChanged = $false
$vddAddedInstanceIds = @()
$vddAddedDriverNames = @()
$createdTask = $false

function Stop-FSDLAgentTasks {
    foreach ($name in @($script:TaskName, 'FSDL USB Bridge Agent')) {
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
    }
}

function Remove-FSDLVddInstances([string[]]$InstanceIds) {
    foreach ($id in $InstanceIds) {
        if (-not $id) { continue }
        & pnputil.exe /remove-device $id | Out-Null
    }
}

try {
    Write-FSDLLog 'Starting OpenTabletDisplay install/update. Existing Sunshine credentials and pairings are preserved.' $logPath
    Stop-FSDLAgentTasks
    $installedExeExisting = Join-Path $script:InstallRoot 'FSDL.exe'
    if (Test-Path $installedExeExisting) {
        try { & $installedExeExisting stop | Out-Null } catch {}
    }

    # Stage and atomically replace FSDL-owned files. Runtime/enrollment are not touched.
    $staging = Join-Path $script:InstallRoot '.staging'
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $staging 'probes') | Out-Null
    Copy-Item -LiteralPath $payloadExe -Destination (Join-Path $staging 'FSDL.exe') -Force
    Copy-Item -Path (Join-Path $payloadProbes '*') -Destination (Join-Path $staging 'probes') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'agent.ps1') -Destination (Join-Path $staging 'agent.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $staging 'uninstall.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'status.ps1') -Destination (Join-Path $staging 'status.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'reset-device.ps1') -Destination (Join-Path $staging 'reset-device.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'common.ps1') -Destination (Join-Path $staging 'common.ps1') -Force

    foreach ($name in @('FSDL.exe','agent.ps1','uninstall.ps1','status.ps1','reset-device.ps1','common.ps1')) {
        Copy-Item -LiteralPath (Join-Path $staging $name) -Destination (Join-Path $script:InstallRoot $name) -Force
    }
    $probeTarget = Join-Path $script:InstallRoot 'probes'
    Remove-Item -LiteralPath $probeTarget -Recurse -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath (Join-Path $staging 'probes') -Destination $probeTarget
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Write-FSDLLog ('Installed OpenTabletDisplay runtime files under ' + $script:InstallRoot) $logPath

    # Sunshine: reuse when present, otherwise install the exact pinned upstream MSI.
    $sunshineService = Get-Service -Name 'SunshineService' -ErrorAction SilentlyContinue
    $sunshineOwned = $false
    if ($oldState -and $oldState.sunshine_installed_by_fsdl) { $sunshineOwned = $true }
    if (-not $sunshineService) {
        Write-FSDLLog ('Sunshine is absent; installing pinned upstream ' + $script:SunshineVersion + '.') $logPath
        $cache = Join-Path $script:InstallRoot 'cache'
        $sunMsi = Join-Path $cache ('Sunshine-' + $script:SunshineVersion + '.msi')
        Get-FSDLVerifiedDownload $script:SunshineUrl $script:SunshineSha256 $sunMsi | Out-Null
        $sig = Get-AuthenticodeSignature -LiteralPath $sunMsi
        if ($sig.Status -ne 'Valid') { throw ('Pinned Sunshine MSI signature is not valid: ' + $sig.Status) }
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', ('"' + $sunMsi + '"'), '/qn', '/norestart') -Wait -PassThru
        if ($proc.ExitCode -ne 0 -and $proc.ExitCode -ne 3010) { throw ('Sunshine MSI failed with exit code ' + $proc.ExitCode) }
        $installedSunshineThisRun = $true
        $sunshineOwned = $true
        Start-Sleep -Seconds 2
        $sunshineService = Get-Service -Name 'SunshineService' -ErrorAction SilentlyContinue
        if (-not $sunshineService) { throw 'Sunshine installation completed but SunshineService was not found.' }

        $sunExe = Get-FSDLSunshineExe
        if (-not $sunExe) { throw 'Could not locate sunshine.exe after install.' }
        $password = New-FSDLRandomPassword
        & $sunExe --creds fsdl $password | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Sunshine credential initialization failed.' }
        $credFile = Join-Path $script:InstallRoot 'sunshine-webui.txt'
        @(
            'Sunshine Web UI credentials created by OpenTabletDisplay on ' + (Get-Date -Format o),
            'URL: https://localhost:47990',
            'Username: fsdl',
            'Password: ' + $password,
            '',
            'Keep this file private. Moonlight pairing uses a separate short PIN.'
        ) | Set-Content -LiteralPath $credFile -Encoding UTF8
        Protect-FSDLUserFile $credFile
        Write-FSDLLog ('Fresh Sunshine credentials saved to ' + $credFile) $logPath
    } else {
        Write-FSDLLog 'Reusing existing Sunshine installation.' $logPath
    }

    if ((Get-Service -Name 'SunshineService').Status -ne 'Running') { Start-Service -Name 'SunshineService' }
    if (-not (Wait-FSDLSunshine 40)) { throw 'Sunshine is installed but /serverinfo did not become healthy.' }

    # Virtual Display Driver: reuse exact ROOT\MttVDD devices, otherwise install pinned signed driver.
    $vddOwned = $false
    if ($oldState -and $oldState.vdd_installed_by_fsdl) { $vddOwned = $true }
    $beforeVdd = @(Get-FSDLVddDevices)
    if ($beforeVdd.Count -eq 0) {
        Write-FSDLLog ('Virtual Display Driver is absent; installing pinned upstream ' + $script:VddVersion + '.') $logPath
        $cache = Join-Path $script:InstallRoot 'cache'
        $vddZip = Join-Path $cache ('VirtualDisplayDriver-' + $script:VddVersion + '.zip')
        $nefZip = Join-Path $cache ('nefcon-' + $script:NefConVersion + '.zip')
        Get-FSDLVerifiedDownload $script:VddUrl $script:VddSha256 $vddZip | Out-Null
        Get-FSDLVerifiedDownload $script:NefConUrl $script:NefConSha256 $nefZip | Out-Null
        $tempRoot = Join-Path $env:TEMP ('fsdl-vdd-' + [Guid]::NewGuid().ToString('N'))
        $vddDir = Join-Path $tempRoot 'vdd'
        $nefDir = Join-Path $tempRoot 'nefcon'
        Expand-FSDLArchive $vddZip $vddDir
        Expand-FSDLArchive $nefZip $nefDir
        $inf = Get-ChildItem -LiteralPath $vddDir -Filter 'MttVDD.inf' -File -Recurse | Select-Object -ExpandProperty FullName -First 1
        $cat = Get-ChildItem -LiteralPath $vddDir -Filter 'mttvdd.cat' -File -Recurse | Select-Object -ExpandProperty FullName -First 1
        $nef = Get-ChildItem -LiteralPath $nefDir -Filter 'nefconw.exe' -File -Recurse | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -ExpandProperty FullName -First 1
        if (-not $inf -or -not $cat -or -not $nef) { throw 'Pinned VDD/NefCon archives did not contain expected install files.' }

        $beforeDrivers = @(Get-FSDLDriverPublishedNames)
        $addedCertThumbprints = New-Object System.Collections.Generic.List[string]
        try {
            $catBytes = [IO.File]::ReadAllBytes($cat)
            $certs = New-Object Security.Cryptography.X509Certificates.X509Certificate2Collection
            $certs.Import($catBytes)
            $certDir = Join-Path $tempRoot 'certs'
            New-Item -ItemType Directory -Force -Path $certDir | Out-Null
            foreach ($cert in $certs) {
                $existing = Get-ChildItem Cert:\LocalMachine\TrustedPublisher -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
                if (-not $existing) {
                    $certPath = Join-Path $certDir ($cert.Thumbprint + '.cer')
                    [IO.File]::WriteAllBytes($certPath, $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
                    Import-Certificate -FilePath $certPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPublisher' | Out-Null
                    $addedCertThumbprints.Add($cert.Thumbprint)
                }
            }
            & $nef install $inf 'Root\MttVDD' | Out-Null
            if ($LASTEXITCODE -ne 0) { throw ('NefCon VDD install failed with exit code ' + $LASTEXITCODE) }
        } finally {
            foreach ($thumb in $addedCertThumbprints) {
                Remove-Item -LiteralPath ('Cert:\LocalMachine\TrustedPublisher\' + $thumb) -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
        # The PnP instance may be ROOT\DISPLAY even when the hardware ID is ROOT\MttVDD.
        # Let PnP settle; never assume a driver is usable just because NefCon exited 0.
        & pnputil.exe /scan-devices | Out-Null
        $afterVdd = @(Wait-FSDLVdd 30)
        # Track only devices and driver packages created by this install
        # before checking health, so a failed install can roll them back.
        $vddAddedInstanceIds = @($afterVdd | Select-Object -ExpandProperty InstanceId)
        $afterDrivers = @(Get-FSDLDriverPublishedNames)
        $vddAddedDriverNames = @($afterDrivers | Where-Object { $beforeDrivers -notcontains $_ })
        $installedVddThisRun = $true
        $vddOwned = $true
        if ($afterVdd.Count -eq 0) {
            Write-FSDLLog 'NefCon returned success but Windows still exposes no ROOT\MttVDD hardware-ID match. Check Device Manager > Display adapters for a disabled or failed driver.' $logPath
            throw 'Virtual Display Driver was not detected after installation; see Device Manager and installer diagnostics.'
        }
        $badVdd = @($afterVdd | Where-Object { $_.Status -ne 'OK' })
        if ($badVdd.Count -gt 0) {
            $desc = ($badVdd | ForEach-Object { $_.InstanceId + ' [' + $_.Status + ']' }) -join '; '
            Write-FSDLLog ('VDD present but not healthy: ' + $desc) $logPath
            throw ('Virtual Display Driver was found but Windows reports it is not healthy: ' + $desc)
        }
    } else {
        foreach ($dev in $beforeVdd) {
            if ($dev.Status -ne 'OK') {
                Enable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction SilentlyContinue
            }
        }
        $verifiedVdd = @(Get-FSDLVddDevices | Where-Object { $_.Status -eq 'OK' })
        if ($verifiedVdd.Count -eq 0) {
            throw 'A Virtual Display Driver is present but not healthy. Check Device Manager before continuing.'
        }
        Write-FSDLLog 'Reusing an existing healthy Virtual Display Driver installation.' $logPath
    }

    Write-FSDLLog 'Restarting Sunshine to enumerate available displays.' $logPath
    Restart-FSDLSunshine
    Write-FSDLLog 'Sunshine is healthy; resolving the virtual display device ID.' $logPath

    # Sunshine emits stable device IDs for all displays. Select the VDD by that ID,
    # never by DISPLAY1/DISPLAY2 numbering or DXGI index. For an in-place update
    # from an older FSDL install, an already-configured GUID is a safe fallback
    # only when the old config strongly matches FSDL's legacy profile.
    $configPath = Get-FSDLSunshineConfigPath
    if (-not $configPath) { throw 'Could not locate Sunshine configuration after install.' }
    $existingLines = @(Get-Content -LiteralPath $configPath)
    $legacyScoreBefore = Get-FSDLLegacyProfileScore $existingLines
    $existingOutput = Get-FSDLConfigEntry $existingLines 'output_name'

    $vddDeviceId = $null
    $lastDiscoveryError = $null
    for ($i = 0; $i -lt 4 -and -not $vddDeviceId; $i++) {
        try {
            $vddDeviceId = Find-FSDLSunshineVirtualDisplayId $OutputDeviceId
        } catch {
            $lastDiscoveryError = $_
            Write-FSDLLog ('Virtual display discovery attempt ' + ($i + 1) + '/4: ' + $_.Exception.Message) $logPath
            if ($i -lt 3) { Start-Sleep -Seconds 2 }
        }
    }
    if (-not $vddDeviceId -and $legacyScoreBefore -ge 6 -and $existingOutput.Present -and ([string]$existingOutput.Value -match '^\{[0-9a-fA-F-]{36}\}$')) {
        $vddDeviceId = [string]$existingOutput.Value
        Write-FSDLLog ('Reusing legacy FSDL Sunshine virtual-display GUID: ' + $vddDeviceId) $logPath
    }
    if (-not $vddDeviceId) {
        if ($lastDiscoveryError) { throw $lastDiscoveryError }
        throw 'Could not discover the Sunshine virtual-display GUID.'
    }
    Write-FSDLLog ('Sunshine virtual display ID: ' + $vddDeviceId) $logPath

    $lines = @($existingLines)
    $backupDir = Join-Path $script:InstallRoot 'backups'
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    $configBackup = Join-Path $backupDir ('sunshine.conf.' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak')
    Copy-Item -LiteralPath $configPath -Destination $configBackup -Force

    $desired = [ordered]@{
        output_name = $vddDeviceId
        dd_configuration_option = 'ensure_primary'
        dd_resolution_option = 'auto'
        dd_refresh_rate_option = 'auto'
        dd_config_revert_on_disconnect = 'enabled'
        dd_config_revert_delay = '1000'
    }
    $changes = New-Object System.Collections.Generic.List[object]
    foreach ($key in $desired.Keys) {
        $orig = Get-FSDLConfigEntry $lines $key
        $changes.Add([pscustomobject]@{
            key = $key
            original_present = [bool]$orig.Present
            original_value = $orig.Value
            applied_present = $true
            applied_value = [string]$desired[$key]
        })
        $lines = Set-FSDLConfigEntry $lines $key $true ([string]$desired[$key])
    }

    # Early FSDL bring-up builds carried a very specific Lenovo/NVENC profile.
    # Remove it only when enough exact signatures match to avoid overwriting a user's tuning.
    $legacyScore = $legacyScoreBefore
    if ($legacyScore -ge 6) {
        foreach ($key in @('dd_manual_resolution','dd_manual_refresh_rate','max_bitrate','minimum_fps_target','encoder','nvenc_preset','controller','gamepad_driver')) {
            $already = @($changes | Where-Object { $_.key -eq $key })
            if ($already.Count -gt 0) { continue }
            $orig = Get-FSDLConfigEntry $lines $key
            if (-not $orig.Present) { continue }
            $changes.Add([pscustomobject]@{
                key = $key
                original_present = $true
                original_value = $orig.Value
                applied_present = $false
                applied_value = $null
            })
            $lines = Set-FSDLConfigEntry $lines $key $false $null
        }
        Write-FSDLLog ('Detected legacy FSDL tuning profile (score ' + $legacyScore + '); returned encoder/input/bitrate choices to Sunshine defaults.') $logPath
    }

    $tmpConfig = $configPath + '.fsdl.tmp'
    [IO.File]::WriteAllLines($tmpConfig, [string[]]$lines, (New-Object Text.UTF8Encoding($false)))
    Move-Item -LiteralPath $tmpConfig -Destination $configPath -Force
    $configChanged = $true
    Write-FSDLLog 'Configured Sunshine for stable VDD capture, client-driven resolution/refresh, session-primary display, and automatic layout restore.' $logPath
    Restart-FSDLSunshine

    # Download and verify USB runtime dependencies now, not on the first daily plug-in.
    # Capture the helper's stdout/stderr instead of hiding its actual failure
    # behind "dependency preparation failed". Antivirus quarantine, network
    # errors and integrity failures must remain distinguishable.
    $installedExe = Join-Path $script:InstallRoot 'FSDL.exe'
    $depOut = Join-Path $script:InstallRoot 'logs\prepare-stdout.txt'
    $depErr = Join-Path $script:InstallRoot 'logs\prepare-stderr.txt'
    Remove-Item -LiteralPath $depOut,$depErr -Force -ErrorAction SilentlyContinue
    Write-FSDLLog 'Preparing verified Android Platform-Tools and gnirehtet dependencies.' $logPath
    try {
        $prepareProc = Start-Process -FilePath $installedExe -ArgumentList 'prepare' -PassThru -WindowStyle Hidden -RedirectStandardOutput $depOut -RedirectStandardError $depErr -ErrorAction Stop
    } catch {
        throw ('Unable to launch FSDL dependency helper (check antivirus protection events, if any): ' + $_.Exception.Message)
    }
    $prepareDone = $prepareProc.WaitForExit(540000)
    if (-not $prepareDone) {
        try { $prepareProc.Kill() } catch {}
        throw 'USB dependency helper exceeded the nine-minute setup deadline. Check prepare-stdout.txt and prepare-stderr.txt in local logs.'
    }
    $prepareProc.Refresh()
    foreach ($entry in @(@('stdout',$depOut),@('stderr',$depErr))) {
        $label = $entry[0]
        $p = $entry[1]
        if (-not (Test-Path -LiteralPath $p)) { continue }
        foreach ($line in @(Get-Content -LiteralPath $p -Tail 80 -ErrorAction SilentlyContinue)) {
            if ($line) { Write-FSDLLog ('USB ' + $label + ': ' + $line) $logPath }
        }
    }
    if ($prepareProc.ExitCode -ne 0) {
        throw ('USB dependency helper failed with exit code ' + $prepareProc.ExitCode + '. See logs\prepare-stderr.txt for the specific error.')
    }

    # Preserve a complete local repair package so Start Menu setup works even
    # after users delete the Downloads ZIP. Never delete arbitrary user files.
    $persistentPackage = Join-Path $script:InstallRoot 'package'
    $sourcePath = [IO.Path]::GetFullPath($packageRoot).TrimEnd('\')
    $targetPath = [IO.Path]::GetFullPath($persistentPackage).TrimEnd('\')
    if ($sourcePath -ine $targetPath) {
        $pendingPackage = Join-Path $script:InstallRoot 'package.pending'
        $previousPackage = Join-Path $script:InstallRoot 'package.previous'
        Remove-Item -LiteralPath $pendingPackage -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path (Join-Path $pendingPackage 'installer'), (Join-Path $pendingPackage 'payload\probes') | Out-Null
        Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File |
            Where-Object { $_.Name -ne 'desktop.ps1' } | Copy-Item -Destination (Join-Path $pendingPackage 'installer') -Force
        Copy-Item -LiteralPath $payloadExe -Destination (Join-Path $pendingPackage 'payload\FSDL.exe') -Force
        Copy-Item -Path (Join-Path $payloadProbes '*') -Destination (Join-Path $pendingPackage 'payload\probes') -Force
        Copy-Item -LiteralPath $desktopExe -Destination (Join-Path $pendingPackage 'OpenTabletDisplay.exe') -Force
        foreach ($name in @('install.cmd','status.cmd','reset-device.cmd','uninstall.cmd')) {
            $source = Join-Path $packageRoot $name
            if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $pendingPackage $name) -Force }
        }
        Remove-Item -LiteralPath $previousPackage -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $persistentPackage) {
            Move-Item -LiteralPath $persistentPackage -Destination $previousPackage
        }
        try {
            Move-Item -LiteralPath $pendingPackage -Destination $persistentPackage
        } catch {
            if (Test-Path -LiteralPath $previousPackage) {
                Move-Item -LiteralPath $previousPackage -Destination $persistentPackage -ErrorAction SilentlyContinue
            }
            throw
        }
        Remove-Item -LiteralPath $previousPackage -Recurse -Force -ErrorAction SilentlyContinue
    }
    Copy-Item -LiteralPath $desktopExe -Destination (Join-Path $script:InstallRoot 'OpenTabletDisplay.exe') -Force
    $brandedAssets = Join-Path $packageRoot 'assets'
    if (-not (Test-Path -LiteralPath (Join-Path $brandedAssets 'OpenTabletDisplay.ico'))) {
        throw 'Release is missing the official Windows icon assets.'
    }
    $installedAssets = Join-Path $script:InstallRoot 'assets'
    New-Item -ItemType Directory -Force -Path $installedAssets | Out-Null
    Copy-Item -Path (Join-Path $brandedAssets '*') -Destination $installedAssets -Force
    $repairAssets = Join-Path $persistentPackage 'assets'
    New-Item -ItemType Directory -Force -Path $repairAssets | Out-Null
    Copy-Item -Path (Join-Path $brandedAssets '*') -Destination $repairAssets -Force
    Write-FSDLLog 'Saved the standalone desktop app and an offline repair package.' $logPath

    $state = [ordered]@{
        version = $script:FSDLVersion
        installed_at = (Get-Date -Format o)
        sunshine_installed_by_fsdl = $sunshineOwned
        vdd_installed_by_fsdl = $vddOwned
        sunshine_config_path = $configPath
        sunshine_backup = $configBackup
        sunshine_changes = @($changes)
        vdd_instance_ids = @()
        vdd_driver_names = @()
    }
    if ($oldState -and $oldState.vdd_instance_ids) { $state.vdd_instance_ids = @($oldState.vdd_instance_ids) }
    if ($oldState -and $oldState.vdd_driver_names) { $state.vdd_driver_names = @($oldState.vdd_driver_names) }
    if ($installedVddThisRun) {
        $state.vdd_instance_ids = @($vddAddedInstanceIds)
        $state.vdd_driver_names = @($vddAddedDriverNames)
    }
    Save-FSDLJson $state $script:InstallStatePath

    # Hidden reconnect agent. Limited privileges are sufficient after installation.
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $agent = Join-Path $script:InstallRoot 'agent.ps1'
    $actionArgs = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $agent + '"'
    $action = New-ScheduledTaskAction -Execute $psExe -Argument $actionArgs
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $script:TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Keeps OpenTabletDisplay wired Android display transport available in the background.' -Force | Out-Null
    Start-ScheduledTask -TaskName $script:TaskName
    $createdTask = $true

    # Register a normal uninstall entry so the extracted release folder can be deleted.
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FSDL'
    New-Item -Path $uninstallKey -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'OpenTabletDisplay' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value $script:FSDLVersion -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name Publisher -Value '0pen-Sourcer' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $script:InstallRoot 'uninstall.ps1') + '"') -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

    # Start Menu shortcut is scoped to the current Windows user only.
    try {
        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'OpenTabletDisplay.lnk'
        $wsh = New-Object -ComObject WScript.Shell
        $shortcut = $wsh.CreateShortcut($menu)
        $shortcut.TargetPath = Join-Path $script:InstallRoot 'OpenTabletDisplay.exe'
        $shortcut.WorkingDirectory = $script:InstallRoot
        $shortcut.Description = 'Use an Android tablet as a wired second Windows screen'
        $shortcut.Save()
    } catch {
        Write-FSDLLog ('Start Menu shortcut could not be created: ' + $_.Exception.Message) $logPath
    }
    Write-FSDLLog 'Install/update completed successfully.' $logPath
    Write-Host ''
    Write-Host 'OpenTabletDisplay is installed.' -ForegroundColor Green
    Write-Host 'One-time Android setup:' -ForegroundColor Cyan
    Write-Host '  1. Install official Moonlight Game Streaming.'
    Write-Host '  2. Enable Developer options -> USB debugging and approve this PC.'
    Write-Host '  3. Keep normal Android USB tethering OFF.'
    Write-Host '  4. Approve gnirehtet''s one-time VPN/Connection request when Android shows it.'
    Write-Host '  5. In Moonlight, set Use touchscreen as a trackpad = OFF for direct touch.'
    Write-Host '  6. Enable Play audio on PC if you want sound on PC speakers and Android together.'
    Write-Host ''
    Write-Host 'After first pairing: plug USB -> open Moonlight -> Desktop. No FSDL console needs to stay open.' -ForegroundColor Green
    if (Test-Path (Join-Path $script:InstallRoot 'sunshine-webui.txt')) {
        Write-Host ('Fresh Sunshine Web UI credentials: ' + (Join-Path $script:InstallRoot 'sunshine-webui.txt'))
    }
    $installLock.Dispose()
    exit 0
}
catch {
    Write-FSDLLog ('INSTALL FAILED: ' + $_.Exception.Message) $logPath
    try { Stop-FSDLAgentTasks } catch {}
    if ($configChanged -and $configBackup -and (Test-Path $configBackup)) {
        try {
            $cfg = Get-FSDLSunshineConfigPath
            if ($cfg) { Copy-Item -LiteralPath $configBackup -Destination $cfg -Force }
            Restart-FSDLSunshine
            Write-FSDLLog 'Rolled back Sunshine configuration.' $logPath
        } catch {}
    }
    if ($installedVddThisRun) {
        try { Remove-FSDLVddInstances $vddAddedInstanceIds } catch {}
        foreach ($driver in $vddAddedDriverNames) {
            try { & pnputil.exe /delete-driver $driver /uninstall | Out-Null } catch {}
        }
    }
    if ($installedSunshineThisRun) {
        try {
            $sunMsi = Join-Path $script:InstallRoot ('cache\Sunshine-' + $script:SunshineVersion + '.msi')
            if (Test-Path $sunMsi) { Start-Process msiexec.exe -ArgumentList @('/x', ('"' + $sunMsi + '"'), '/qn', '/norestart') -Wait | Out-Null }
        } catch {}
    }
    throw
}
finally {
    if ($installLock) { $installLock.Dispose() }
}
