Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:FSDLVersion = '3.2.0-rc.1'
$script:TaskName = 'FSDL USB Display Agent'
$script:InstallRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FSDL'
$script:InstallStatePath = Join-Path $script:InstallRoot 'install-state.json'
$script:HostIdentityPath = Join-Path $script:InstallRoot 'host-identity.json'
$script:SunshineVersion = '2026.914.233613'
$script:SunshineUrl = 'https://github.com/LizardByte/Sunshine/releases/download/v2026.914.233613/Sunshine-Windows-AMD64-installer.msi'
$script:SunshineSha256 = '1d7fed8beecd5889dc7ff14cf9f42d6d38f37c3066c13c6c2a5f4e91847e0ccf'
$script:VddVersion = '25.7.23'
$script:VddUrl = 'https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip'
$script:VddSha256 = 'e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a'
$script:NefConVersion = '1.14.0'
$script:NefConUrl = 'https://github.com/nefarius/nefcon/releases/download/v1.14.0/nefcon_v1.14.0.zip'
$script:NefConSha256 = 'a15557da24a9efca203158de3b43b0eaf982db231f0194031f1ed428bc13e669'

function Test-FSDLAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-FSDLElevatedSelf([string]$ScriptPath, [string[]]$ForwardArgs) {
    $parts = New-Object System.Collections.Generic.List[string]
    $parts.Add('-NoProfile')
    $parts.Add('-ExecutionPolicy')
    $parts.Add('Bypass')
    $parts.Add('-File')
    $parts.Add(('"' + $ScriptPath + '"'))
    foreach ($arg in $ForwardArgs) {
        if ($null -ne $arg -and $arg -ne '') {
            $parts.Add($arg)
        }
    }
    $p = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList ($parts -join ' ') -Wait -PassThru
    return $p.ExitCode
}

function Write-FSDLLog([string]$Message, [string]$LogPath) {
    $line = '[' + (Get-Date -Format 'HH:mm:ss') + '] ' + $Message
    Write-Host $line
    if ($LogPath) {
        $dir = Split-Path -Parent $LogPath
        if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    }
}

function Enable-FSDLTls12 {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    } catch {}
}

function Get-FSDLSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-FSDLVerifiedDownload([string]$Url, [string]$Sha256, [string]$Destination) {
    Enable-FSDLTls12
    $dir = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $part = $Destination + '.part'
    Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $part
    $got = Get-FSDLSha256 $part
    if ($got -ne $Sha256.ToLowerInvariant()) {
        Remove-Item -LiteralPath $part -Force -ErrorAction SilentlyContinue
        throw ('SHA-256 mismatch for ' + $Url + ': expected ' + $Sha256 + ', got ' + $got)
    }
    Move-Item -LiteralPath $part -Destination $Destination -Force
    return $Destination
}

function Expand-FSDLArchive([string]$ZipPath, [string]$Destination) {
    Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination -Force
}

function Get-FSDLSunshineConfigPath {
    $direct = Join-Path $env:ProgramFiles 'Sunshine\config\sunshine.conf'
    if (Test-Path $direct) { return $direct }
    $dir = Join-Path $env:ProgramFiles 'Sunshine\config'
    if (Test-Path $dir) {
        $candidate = Get-ChildItem -LiteralPath $dir -Filter '*.conf' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    return $null
}

function Get-FSDLConfigEntry([string[]]$Lines, [string]$Key) {
    $rx = '^\s*' + [regex]::Escape($Key) + '\s*=\s*(.*)$'
    foreach ($line in $Lines) {
        $m = [regex]::Match($line, $rx)
        if ($m.Success) {
            return [pscustomobject]@{ Present = $true; Value = $m.Groups[1].Value.Trim() }
        }
    }
    return [pscustomobject]@{ Present = $false; Value = $null }
}

function Set-FSDLConfigEntry([string[]]$Lines, [string]$Key, [bool]$Present, [AllowNull()][string]$Value) {
    $rx = '^\s*' + [regex]::Escape($Key) + '\s*='
    $out = New-Object System.Collections.Generic.List[string]
    $written = $false
    foreach ($line in $Lines) {
        if ($line -match $rx) {
            if ($Present -and -not $written) {
                $out.Add($Key + ' = ' + $Value)
                $written = $true
            }
            continue
        }
        $out.Add($line)
    }
    if ($Present -and -not $written) {
        $out.Add($Key + ' = ' + $Value)
    }
    return $out.ToArray()
}

function Test-FSDLConfigEntryEquals($Entry, [bool]$Present, [AllowNull()][string]$Value) {
    if ([bool]$Entry.Present -ne $Present) { return $false }
    if (-not $Present) { return $true }
    return ([string]$Entry.Value -eq [string]$Value)
}

function Wait-FSDLSunshine([int]$Seconds = 35) {
    for ($i = 0; $i -lt $Seconds; $i++) {
        try {
            $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:47989/serverinfo' -TimeoutSec 2
            if ($r.StatusCode -eq 200) { return $true }
        } catch {}
        Start-Sleep -Seconds 1
    }
    return $false
}

function Restart-FSDLSunshine {
    $svc = Get-Service -Name 'SunshineService' -ErrorAction SilentlyContinue
    if (-not $svc) { throw 'SunshineService is not installed.' }
    Stop-Service -Name 'SunshineService' -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    Start-Service -Name 'SunshineService'
    if (-not (Wait-FSDLSunshine 40)) {
        throw 'Sunshine did not return HTTP 200 after restart.'
    }
}

function Get-FSDLSunshineExe {
    $p = Join-Path $env:ProgramFiles 'Sunshine\sunshine.exe'
    if (Test-Path $p) { return $p }
    $candidate = Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles 'Sunshine') -Filter 'sunshine.exe' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
    return $null
}

function New-FSDLRandomPassword([int]$Bytes = 24) {
    $buf = New-Object byte[] $Bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buf) } finally { $rng.Dispose() }
    $s = [Convert]::ToBase64String($buf).TrimEnd('=').Replace('+','A').Replace('/','B')
    return $s
}

function Protect-FSDLUserFile([string]$Path) {
    try {
        $user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        & icacls.exe $Path '/inheritance:r' '/grant:r' ($user + ':F') | Out-Null
    } catch {}
}

function Get-FSDLVddDevices {
    $devices = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -match '^ROOT\\MTTVDD' })
    return $devices
}

function Get-FSDLDriverPublishedNames {
    try {
        return @(Get-WindowsDriver -Online -ErrorAction Stop | Where-Object {
            ([string]$_.OriginalFileName -match '(?i)MttVDD\.inf$')
        } | Select-Object -ExpandProperty Driver)
    } catch {
        return @()
    }
}

function Get-FSDLSunshineLogCandidates {
    $paths = New-Object System.Collections.Generic.List[string]
    $roots = @(
        (Join-Path $env:ProgramFiles 'Sunshine'),
        (Join-Path $env:ProgramData 'Sunshine'),
        $env:TEMP,
        (Join-Path $env:SystemRoot 'Temp')
    )
    foreach ($root in $roots) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        Get-ChildItem -LiteralPath $root -Filter '*.log' -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Name -match '(?i)sunshine') { $paths.Add($_.FullName) }
        }
    }
    return @($paths | Sort-Object -Unique)
}

function Find-FSDLSunshineVirtualDisplayId([string]$ExplicitId) {
    if ($ExplicitId) {
        if ($ExplicitId -notmatch '^\{[0-9a-fA-F-]{36}\}$') { throw 'OutputDeviceId must look like {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}.' }
        return $ExplicitId
    }

    $records = @{}
    foreach ($log in (Get-FSDLSunshineLogCandidates)) {
        try { $text = Get-Content -LiteralPath $log -Raw -ErrorAction Stop } catch { continue }
        $matches = [regex]::Matches($text, '"device_id"\s*:\s*"(?<id>\{[0-9a-fA-F-]{36}\})"(?s:.{0,700}?)"friendly_name"\s*:\s*"(?<name>[^"]*)"')
        foreach ($m in $matches) {
            $id = $m.Groups['id'].Value
            $name = $m.Groups['name'].Value
            if ($name -match '(?i)(virtual\s*display|virtual.*driver|idd\s*hdr|mtt)') {
                $records[$id] = $name
            }
        }
    }
    $ids = @($records.Keys)
    if ($ids.Count -eq 1) { return [string]$ids[0] }
    if ($ids.Count -gt 1) {
        $detail = ($ids | ForEach-Object { $_ + ' (' + $records[$_] + ')' }) -join ', '
        throw ('More than one Sunshine virtual-display candidate was found: ' + $detail + '. Re-run installer with -OutputDeviceId {GUID}.')
    }
    throw 'Could not discover the Virtual Display Driver in Sunshine display enumeration logs. Restart Sunshine once or re-run installer with -OutputDeviceId {GUID}.'
}

function Get-FSDLLegacyProfileScore([string[]]$Lines) {
    $sig = [ordered]@{
        dd_resolution_option = 'manual'
        dd_manual_resolution = '1280x800'
        dd_refresh_rate_option = 'manual'
        dd_manual_refresh_rate = '60'
        max_bitrate = '40000'
        minimum_fps_target = '60'
        nvenc_preset = '1'
        controller = 'disabled'
        gamepad_driver = 'none'
    }
    $score = 0
    foreach ($key in $sig.Keys) {
        $e = Get-FSDLConfigEntry $Lines $key
        if ($e.Present -and ([string]$e.Value -eq [string]$sig[$key])) { $score++ }
    }
    return $score
}

function Save-FSDLJson([object]$Value, [string]$Path) {
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
}


function Get-OTDHostIdentity {
    if (-not (Test-Path $script:HostIdentityPath)) { return $null }
    try {
        $identity = Get-Content -LiteralPath $script:HostIdentityPath -Raw | ConvertFrom-Json
        if ([int]$identity.schema -ne 1 -or [string]$identity.mode -ne 'loopback_alias' -or -not [bool]$identity.managed_by_opentabletdisplay) { return $null }
        if ([string]$identity.ip -notmatch '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[0-1])\.)') { return $null }
        if ([int]$identity.prefix_length -ne 32) { return $null }
        return $identity
    } catch { return $null }
}

function Get-OTDLoopbackInterfaceIndex {
    $loop = Get-NetIPAddress -AddressFamily IPv4 -IPAddress '127.0.0.1' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $loop) { throw 'Windows IPv4 loopback interface (127.0.0.1) was not found.' }
    return [uint32]$loop.InterfaceIndex
}

function Test-OTDHostRouteConflict([string]$IPAddress, [uint32]$LoopbackIndex) {
    if (Get-NetIPAddress -AddressFamily IPv4 -IPAddress $IPAddress -ErrorAction SilentlyContinue) { return $true }
    try {
        $routeObjects = @(Find-NetRoute -RemoteIPAddress $IPAddress -ErrorAction Stop)
        $route = @($routeObjects | Where-Object { $_.PSObject.Properties.Name -contains 'DestinationPrefix' } | Select-Object -First 1)
        if ($route.Count -eq 0) { return $false }
        $prefix = [string]$route[0].DestinationPrefix
        $index = [uint32]$route[0].InterfaceIndex
        return ($prefix -ne '0.0.0.0/0' -and $index -ne $LoopbackIndex)
    } catch {
        return $false
    }
}

function New-OTDHostIdentity {
    $loopIndex = Get-OTDLoopbackInterfaceIndex
    $existing = Get-OTDHostIdentity
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($existing -and $existing.ip) { $candidates.Add([string]$existing.ip) }
    foreach ($candidate in @('172.31.255.254','172.30.255.254','10.255.255.254','192.168.255.254')) {
        if (-not $candidates.Contains($candidate)) { $candidates.Add($candidate) }
    }
    foreach ($candidate in $candidates) {
        $already = @(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $candidate -ErrorAction SilentlyContinue)
        $ours = @($already | Where-Object { [uint32]$_.InterfaceIndex -eq $loopIndex -and [int]$_.PrefixLength -eq 32 })
        if ($ours.Count -gt 0 -and $existing -and [string]$existing.ip -eq $candidate -and [uint32]$existing.interface_index -eq $loopIndex) {
            return $existing
        }
        if ($already.Count -gt 0) { continue }
        if (Test-OTDHostRouteConflict $candidate $loopIndex) { continue }
        try {
            New-NetIPAddress -InterfaceIndex $loopIndex -IPAddress $candidate -PrefixLength 32 -AddressFamily IPv4 -SkipAsSource $true -ErrorAction Stop | Out-Null
            Start-Sleep -Milliseconds 250
            $check = @(Get-NetIPAddress -InterfaceIndex $loopIndex -AddressFamily IPv4 -IPAddress $candidate -ErrorAction SilentlyContinue | Where-Object { [int]$_.PrefixLength -eq 32 })
            if ($check.Count -eq 0) { throw 'Windows did not retain the new /32 address.' }
            $identity = [ordered]@{
                schema = 1
                ip = $candidate
                mode = 'loopback_alias'
                interface_index = $loopIndex
                prefix_length = 32
                skip_as_source = $true
                managed_by_opentabletdisplay = $true
                created_at = (Get-Date -Format o)
            }
            Save-FSDLJson $identity $script:HostIdentityPath
            return [pscustomobject]$identity
        } catch {
            Remove-NetIPAddress -InterfaceIndex $loopIndex -IPAddress $candidate -Confirm:$false -ErrorAction SilentlyContinue
        }
    }
    throw 'No conflict-free RFC1918 /32 address was available for the OpenTabletDisplay host-only identity.'
}

function Remove-OTDHostIdentity($Identity) {
    if (-not $Identity -or -not [bool]$Identity.managed_by_opentabletdisplay) { return $false }
    $ip = [string]$Identity.ip
    $index = [uint32]$Identity.interface_index
    $owned = @(Get-NetIPAddress -InterfaceIndex $index -AddressFamily IPv4 -IPAddress $ip -ErrorAction SilentlyContinue | Where-Object {
        [int]$_.PrefixLength -eq 32 -and [bool]$_.SkipAsSource
    })
    if ($owned.Count -eq 0) { return $false }
    Remove-NetIPAddress -InterfaceIndex $index -IPAddress $ip -Confirm:$false -ErrorAction SilentlyContinue
    return $true
}

function Test-OTDSunshineHostIdentity([string]$IPAddress) {
    try {
        $uri = 'http://' + $IPAddress + ':47989/serverinfo'
        $r = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 4
        if ($r.StatusCode -ne 200) { return $false }
        [xml]$xml = $r.Content
        return ([string]$xml.root.LocalIP -eq $IPAddress)
    } catch { return $false }
}
