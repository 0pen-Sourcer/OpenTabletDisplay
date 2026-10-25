from pathlib import Path


def replace_exact(path, old, new, expected=1):
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise SystemExit(f"{path}: expected {expected} occurrence(s) of {old!r}, found {count}")
    p.write_text(text.replace(old, new), encoding="utf-8")


replace_exact("internal/bridge/constants.go", 'Version = "3.1.1"', 'Version = "3.2.0-rc.1"')
replace_exact("installer/common.ps1", "$script:FSDLVersion = '3.1.1'", "$script:FSDLVersion = '3.2.0-rc.1'")
replace_exact(
    "installer/common.ps1",
    "$script:InstallStatePath = Join-Path $script:InstallRoot 'install-state.json'",
    "$script:InstallStatePath = Join-Path $script:InstallRoot 'install-state.json'\n$script:HostIdentityPath = Join-Path $script:InstallRoot 'host-identity.json'",
)

Path("internal/bridge/host_identity.go").write_text(
    r'''package bridge

import (
    "encoding/json"
    "fmt"
    "net"
    "os"
    "path/filepath"
    "strings"
)

const hostIdentityFileName = "host-identity.json"

type hostIdentityConfig struct {
    Schema                     int    `json:"schema"`
    IP                         string `json:"ip"`
    Mode                       string `json:"mode"`
    InterfaceIndex             uint32 `json:"interface_index"`
    PrefixLength               int    `json:"prefix_length"`
    ManagedByOpenTabletDisplay bool   `json:"managed_by_opentabletdisplay"`
}

func loadConfiguredHostIdentity(root string) (hostIdentityConfig, bool, error) {
    path := filepath.Join(root, hostIdentityFileName)
    b, err := os.ReadFile(path)
    if err != nil {
        if os.IsNotExist(err) {
            return hostIdentityConfig{}, false, nil
        }
        return hostIdentityConfig{}, false, fmt.Errorf("read managed host identity: %w", err)
    }
    var cfg hostIdentityConfig
    if err := json.Unmarshal(b, &cfg); err != nil {
        return hostIdentityConfig{}, false, fmt.Errorf("parse managed host identity: %w", err)
    }
    cfg.IP = strings.TrimSpace(cfg.IP)
    parsed := net.ParseIP(cfg.IP)
    if cfg.Schema != 1 || cfg.Mode != "loopback_alias" || !cfg.ManagedByOpenTabletDisplay {
        return hostIdentityConfig{}, false, fmt.Errorf("managed host identity file has unsupported ownership/schema")
    }
    if parsed == nil || parsed.To4() == nil || !parsed.IsPrivate() || parsed.IsLoopback() || parsed.IsLinkLocalUnicast() || cfg.PrefixLength != 32 {
        return hostIdentityConfig{}, false, fmt.Errorf("managed host identity contains unsafe IPv4 %q", cfg.IP)
    }
    return cfg, true, nil
}

func configuredHostIPv4(root string) (string, bool, error) {
    cfg, ok, err := loadConfiguredHostIdentity(root)
    if err != nil || !ok {
        return "", ok, err
    }
    return cfg.IP, true, nil
}

func isConfiguredHostIPv4(root, ip string) bool {
    configured, ok, err := configuredHostIPv4(root)
    return err == nil && ok && configured == strings.TrimSpace(ip)
}
''',
    encoding="utf-8",
)

Path("internal/bridge/host_identity_test.go").write_text(
    r'''package bridge

import (
    "os"
    "path/filepath"
    "testing"
)

func writeIdentity(t *testing.T, root, body string) {
    t.Helper()
    if err := os.WriteFile(filepath.Join(root, hostIdentityFileName), []byte(body), 0o600); err != nil {
        t.Fatal(err)
    }
}

func TestConfiguredHostIdentityPrivateAlias(t *testing.T) {
    root := t.TempDir()
    writeIdentity(t, root, `{"schema":1,"ip":"172.31.255.254","mode":"loopback_alias","interface_index":1,"prefix_length":32,"managed_by_opentabletdisplay":true}`)
    ip, ok, err := configuredHostIPv4(root)
    if err != nil || !ok || ip != "172.31.255.254" {
        t.Fatalf("got ip=%q ok=%v err=%v", ip, ok, err)
    }
}

func TestConfiguredHostIdentityRejectsUnsafeAddresses(t *testing.T) {
    for _, ip := range []string{"127.0.0.2", "8.8.8.8", "169.254.5.5"} {
        t.Run(ip, func(t *testing.T) {
            root := t.TempDir()
            writeIdentity(t, root, `{"schema":1,"ip":"`+ip+`","mode":"loopback_alias","interface_index":1,"prefix_length":32,"managed_by_opentabletdisplay":true}`)
            if _, ok, err := configuredHostIPv4(root); err == nil || ok {
                t.Fatalf("unsafe identity %s was accepted", ip)
            }
        })
    }
}

func TestConfiguredHostIdentityMissingIsFallbackEligible(t *testing.T) {
    if ip, ok, err := configuredHostIPv4(t.TempDir()); err != nil || ok || ip != "" {
        t.Fatalf("got ip=%q ok=%v err=%v", ip, ok, err)
    }
}
''',
    encoding="utf-8",
)

replace_exact("internal/bridge/tether_windows.go", '"net"\n\t"strings"', '"net"\n\t"os"\n\t"strings"')
old = '''// detectHostIPv4 selects the Windows address that Sunshine should advertise to Moonlight.
// The address itself is routed through gnirehtet over USB, so the tablet does NOT use Wi-Fi
// to reach it. We deliberately avoid RNDIS/virtual/VPN adapters and prefer the physical
// interface that owns the best default route (normally the user's Wi-Fi adapter).
func detectHostIPv4(ctx context.Context, r Runner) (string, error) {'''
new = '''// detectHostIPv4 prefers the stable OpenTabletDisplay-owned loopback /32 identity.
// FSDL/OTD 3.1.x physical-address discovery remains as a conservative fallback when the
// managed alias is unavailable or explicitly bypassed for a live-session retry.
func detectHostIPv4(ctx context.Context, r Runner, root string) (string, error) {
	if os.Getenv("OTD_FORCE_PHYSICAL") != "1" {
		if configured, ok, err := configuredHostIPv4(root); err != nil {
			return "", err
		} else if ok {
			script := fmt.Sprintf(`if (Get-NetIPAddress -AddressFamily IPv4 -IPAddress '%s' -ErrorAction SilentlyContinue) { 'PRESENT' } else { exit 3 }`, configured)
			out, er, runErr := r.Run(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script)
			if runErr != nil || !strings.Contains(out, "PRESENT") {
				return "", fmt.Errorf("managed OpenTabletDisplay host identity %s is not present on Windows: %v (%s)", configured, runErr, strings.TrimSpace(er))
			}
			return configured, nil
		}
	}
	return detectPhysicalHostIPv4(ctx, r)
}

// detectPhysicalHostIPv4 is the validated 3.1.x fallback. The address itself is still
// routed through gnirehtet over USB; it only requires the host to have a physical IPv4.
func detectPhysicalHostIPv4(ctx context.Context, r Runner) (string, error) {'''
replace_exact("internal/bridge/tether_windows.go", old, new)

replace_exact(
    "internal/bridge/tether_other.go",
    '''func detectHostIPv4(ctx context.Context, r Runner) (string, error) {
	if v := os.Getenv("FSDL_TEST_HOST_IP"); v != "" {
		return v, nil
	}
	return "192.168.50.23", nil
}''',
    '''func detectHostIPv4(ctx context.Context, r Runner, root string) (string, error) {
	if v := os.Getenv("FSDL_TEST_HOST_IP"); v != "" {
		return v, nil
	}
	if os.Getenv("OTD_FORCE_PHYSICAL") != "1" {
		if ip, ok, err := configuredHostIPv4(root); err != nil {
			return "", err
		} else if ok {
			return ip, nil
		}
	}
	return "192.168.50.23", nil
}''',
)

marker = 'func (a *App) validateSunshineAtHostIP(ctx context.Context, hostIP string) error {'
app_path = Path("internal/bridge/app.go")
app = app_path.read_text(encoding="utf-8")
if marker not in app:
    raise SystemExit("app.go select marker missing")
insert_after = '\treturn validateServerInfoAddress(wire, hostIP)\n}\n'
helper = r'''

func (a *App) selectSunshineHostIP(ctx context.Context) (string, error) {
	configuredIP, configured, configuredErr := configuredHostIPv4(a.Root)
	hostIP, err := detectHostIPv4(ctx, a.Runner, a.Root)
	if err == nil {
		if validateErr := a.validateSunshineAtHostIP(ctx, hostIP); validateErr == nil {
			return hostIP, nil
		} else {
			err = validateErr
		}
	}
	if os.Getenv("OTD_FORCE_PHYSICAL") == "1" || (!configured && configuredErr == nil) {
		return "", fmt.Errorf("selected Moonlight host address is not Sunshine-safe: %w", err)
	}
	label := configuredIP
	if label == "" {
		label = "configured host-only identity"
	}
	a.Out("  Managed host identity %s is unavailable for this session: %v", label, err)
	a.Out("  Falling back to the proven physical-address path for this session.")
	previous, hadPrevious := os.LookupEnv("OTD_FORCE_PHYSICAL")
	_ = os.Setenv("OTD_FORCE_PHYSICAL", "1")
	defer func() {
		if hadPrevious {
			_ = os.Setenv("OTD_FORCE_PHYSICAL", previous)
		} else {
			_ = os.Unsetenv("OTD_FORCE_PHYSICAL")
		}
	}()
	physical, physicalErr := detectHostIPv4(ctx, a.Runner, a.Root)
	if physicalErr != nil {
		return "", fmt.Errorf("host-only identity failed (%v) and physical fallback is unavailable: %w", err, physicalErr)
	}
	if validateErr := a.validateSunshineAtHostIP(ctx, physical); validateErr != nil {
		return "", fmt.Errorf("host-only identity failed (%v) and physical fallback is not Sunshine-safe: %w", err, validateErr)
	}
	return physical, nil
}
'''
start = app.index(marker)
pos = app.index(insert_after, start) + len(insert_after)
app_path.write_text(app[:pos] + helper + app[pos:], encoding="utf-8")

replace_exact(
    "internal/bridge/app.go",
    '''hostIP, err := detectHostIPv4(ctx, a.Runner)
	if err != nil {
		return err
	}
	if err := a.validateSunshineAtHostIP(ctx, hostIP); err != nil {
		return fmt.Errorf("selected Moonlight host address is not Sunshine-safe: %w", err)
	}''',
    '''hostIP, err := a.selectSunshineHostIP(ctx)
	if err != nil {
		return err
	}''',
)

old_cleanup = '''\tcleanupSession := true
\tdefer func() {
\t\tif cleanupSession {
\t\t\t// Failure rollback: do not leave a VPN client, adb reverse mapping, or relay behind.
\t\t\t_, _, _ = a.Runner.Run(context.Background(), gni, "stop", serial)
\t\t\t_, _, _ = a.Runner.Run(context.Background(), adb, "-s", serial, "reverse", "--remove", "localabstract:gnirehtet")
\t\t\t_ = KillVerifiedProcess(pid, gni)
\t\t}
\t}()'''.replace('\\t', '\t')
new_cleanup = '''\tcleanupSession := true
\tcleanupTransport := func() {
\t\t_, _, _ = a.Runner.Run(context.Background(), gni, "stop", serial)
\t\t_, _, _ = a.Runner.Run(context.Background(), adb, "-s", serial, "reverse", "--remove", "localabstract:gnirehtet")
\t\t_ = KillVerifiedProcess(pid, gni)
\t}
\tdefer func() {
\t\tif cleanupSession {
\t\t\t// Failure rollback: do not leave a VPN client, adb reverse mapping, or relay behind.
\t\t\tcleanupTransport()
\t\t}
\t}()
\tfallbackToPhysical := func(cause error) error {
\t\tif os.Getenv("OTD_FORCE_PHYSICAL") == "1" || !isConfiguredHostIPv4(a.Root, hostIP) {
\t\t\treturn cause
\t\t}
\t\ta.Out("  Managed host-only identity %s failed live Android validation: %v", hostIP, cause)
\t\ta.Out("  Cleaning that attempt and retrying the proven physical-address path.")
\t\tcleanupTransport()
\t\tcleanupSession = false
\t\tprevious, hadPrevious := os.LookupEnv("OTD_FORCE_PHYSICAL")
\t\t_ = os.Setenv("OTD_FORCE_PHYSICAL", "1")
\t\tdefer func() {
\t\t\tif hadPrevious {
\t\t\t\t_ = os.Setenv("OTD_FORCE_PHYSICAL", previous)
\t\t\t} else {
\t\t\t\t_ = os.Unsetenv("OTD_FORCE_PHYSICAL")
\t\t\t}
\t\t}()
\t\treturn a.Start(ctx)
\t}'''.replace('\\t', '\t')
replace_exact("internal/bridge/app.go", old_cleanup, new_cleanup)
replace_exact(
    "internal/bridge/app.go",
    'return fmt.Errorf("USB TCP self-test failed: %w", err)',
    'return fallbackToPhysical(fmt.Errorf("USB TCP self-test failed: %w", err))',
)
replace_exact(
    "internal/bridge/app.go",
    'return fmt.Errorf("USB UDP self-test failed: %w", err)',
    'return fallbackToPhysical(fmt.Errorf("USB UDP self-test failed: %w", err))',
)

for old_s, new_s in [
    ('a.Out("FSDL USB Bridge %s", Version)', 'a.Out("OpenTabletDisplay USB Bridge %s", Version)'),
    ('a.Out("FSDL USB bridge stopped. Sunshine/VDD were not touched.")', 'a.Out("OpenTabletDisplay USB bridge stopped. Sunshine/VDD were not touched.")'),
    ('a.Out("FSDL %s dependency preparation", Version)', 'a.Out("OpenTabletDisplay %s dependency preparation", Version)'),
    ('a.Out("FSDL USB Bridge doctor")', 'a.Out("OpenTabletDisplay USB Bridge doctor")'),
]:
    replace_exact("internal/bridge/app.go", old_s, new_s)

replace_exact(
    "internal/bridge/daemon.go",
    '''\thostIP, err := detectHostIPv4(ctx, a.Runner)
\tif err != nil || hostIP != st.HostIP {
\t\treturn false
\t}'''.replace('\\t', '\t'),
    '''\tif err := a.validateSunshineAtHostIP(ctx, st.HostIP); err != nil {
\t\treturn false
\t}'''.replace('\\t', '\t'),
)
for old_s, new_s in [
    ("Android USB tethering is ON; FSDL is paused until it is OFF.", "Android USB tethering is ON; OpenTabletDisplay is paused until it is OFF."),
    ("establishing FSDL transport...", "establishing OpenTabletDisplay transport..."),
    ("FSDL USB transport READY in background.", "OpenTabletDisplay USB transport READY in background."),
]:
    replace_exact("internal/bridge/daemon.go", old_s, new_s)

common_append = r'''

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
'''
p = Path("installer/common.ps1")
p.write_text(p.read_text(encoding="utf-8") + common_append, encoding="utf-8")

replace_exact("installer/install.ps1", "$createdTask = $false", "$createdTask = $false\n$hostIdentity = $null\n$hostIdentityCreatedThisRun = $false")
identity_block = r'''

    # Create a stable project-owned RFC1918 /32 on the Windows loopback interface.
    # This removes the need to borrow a Wi-Fi/Ethernet address as Moonlight identity.
    $identityBefore = Get-OTDHostIdentity
    $identityAddressBefore = $false
    if ($identityBefore) {
        $identityAddressBefore = [bool](Get-NetIPAddress -InterfaceIndex ([uint32]$identityBefore.interface_index) -AddressFamily IPv4 -IPAddress ([string]$identityBefore.ip) -ErrorAction SilentlyContinue)
    }
    try {
        $hostIdentity = New-OTDHostIdentity
        if (-not $identityAddressBefore) { $hostIdentityCreatedThisRun = $true }
        if (Test-OTDSunshineHostIdentity ([string]$hostIdentity.ip)) {
            Write-FSDLLog ('Managed host-only Moonlight identity: ' + $hostIdentity.ip + '/32 on Windows loopback.') $logPath
        } else {
            Write-FSDLLog ('Managed host-only identity ' + $hostIdentity.ip + ' did not produce matching Sunshine LocalIP; removing it and keeping the 3.1.x physical-address fallback.') $logPath
            [void](Remove-OTDHostIdentity $hostIdentity)
            Remove-Item -LiteralPath $script:HostIdentityPath -Force -ErrorAction SilentlyContinue
            $hostIdentity = $null
            $hostIdentityCreatedThisRun = $false
        }
    } catch {
        Write-FSDLLog ('Host-only identity setup unavailable; preserving physical-address fallback: ' + $_.Exception.Message) $logPath
        $hostIdentity = $null
    }
'''
replace_exact(
    "installer/install.ps1",
    "    # Download and verify USB runtime dependencies now, not on the first daily plug-in.\n",
    identity_block + "\n    # Download and verify USB runtime dependencies now, not on the first daily plug-in.\n",
)
replace_exact(
    "installer/install.ps1",
    "if ($LASTEXITCODE -ne 0) { throw 'FSDL dependency preparation failed.' }",
    "if ($LASTEXITCODE -ne 0) { throw 'OpenTabletDisplay dependency preparation failed.' }",
)
replace_exact(
    "installer/install.ps1",
    "        vdd_driver_names = @()\n    }",
    "        vdd_driver_names = @()\n        host_identity_ip = if ($hostIdentity) { [string]$hostIdentity.ip } else { $null }\n        host_identity_mode = if ($hostIdentity) { 'loopback_alias' } else { 'physical_fallback' }\n    }",
)
replace_exact(
    "installer/install.ps1",
    "    Write-Host 'After first pairing: plug USB -> open Moonlight -> Desktop. No FSDL console needs to stay open.' -ForegroundColor Green",
    "    if ($hostIdentity) { Write-Host ('Stable USB-only host identity: ' + $hostIdentity.ip) -ForegroundColor Green } else { Write-Host 'Host identity: physical IPv4 fallback (an active host network address is still required).' -ForegroundColor Yellow }\n    Write-Host 'After first pairing: plug USB -> open Moonlight -> Desktop. No OpenTabletDisplay console needs to stay open.' -ForegroundColor Green",
)
replace_exact(
    "installer/install.ps1",
    "    try { Stop-FSDLAgentTasks } catch {}\n",
    "    try { Stop-FSDLAgentTasks } catch {}\n    if ($hostIdentityCreatedThisRun) {\n        try {\n            $rollbackIdentity = Get-OTDHostIdentity\n            if ($rollbackIdentity) { [void](Remove-OTDHostIdentity $rollbackIdentity) }\n            Remove-Item -LiteralPath $script:HostIdentityPath -Force -ErrorAction SilentlyContinue\n            Write-FSDLLog 'Rolled back the host-only loopback identity.' $logPath\n        } catch {}\n    }\n",
)

uninstall_block = r'''

$hostIdentity = Get-OTDHostIdentity
if ($hostIdentity) {
    if (Remove-OTDHostIdentity $hostIdentity) {
        Write-FSDLLog ('Removed OpenTabletDisplay-owned host identity ' + $hostIdentity.ip + '/32.') $logPath
    } else {
        Write-FSDLLog 'Managed host identity record existed, but the address no longer matched the OpenTabletDisplay ownership fingerprint; preserving the current network state.' $logPath
    }
    Remove-Item -LiteralPath $script:HostIdentityPath -Force -ErrorAction SilentlyContinue
}
'''
replace_exact(
    "installer/uninstall.ps1",
    "# Restore only keys that still equal FSDL's applied value.",
    uninstall_block + "\n# Restore only keys that still equal FSDL's applied value.",
)

status_block = r'''$hostIdentity = Get-OTDHostIdentity
if ($hostIdentity) {
    $present = @(Get-NetIPAddress -InterfaceIndex ([uint32]$hostIdentity.interface_index) -AddressFamily IPv4 -IPAddress ([string]$hostIdentity.ip) -ErrorAction SilentlyContinue | Where-Object { [int]$_.PrefixLength -eq 32 })
    if ($present.Count -gt 0) {
        Write-Host ('Host identity: ' + $hostIdentity.ip + '/32  managed loopback')
    } else {
        Write-Host ('Host identity: ' + $hostIdentity.ip + '/32  MISSING') -ForegroundColor Yellow
    }
} else {
    Write-Host 'Host identity: physical IPv4 fallback' -ForegroundColor Yellow
}

'''
replace_exact(
    "installer/status.ps1",
    "$enrollment = Join-Path $script:InstallRoot 'enrollment.json'\n",
    status_block + "$enrollment = Join-Path $script:InstallRoot 'enrollment.json'\n",
)

replace_exact(
    "CHANGELOG.md",
    "# Changelog\n\n",
    "# Changelog\n\n## 3.2.0-rc.1 — Host-only identity candidate\n\n- Adds an OpenTabletDisplay-owned private `/32` on the Windows loopback interface so Moonlight/Sunshine no longer need to borrow the host Wi-Fi/Ethernet address when the alias passes validation.\n- Chooses from conflict-checked RFC1918 candidates, marks the alias `SkipAsSource`, persists ownership metadata, and removes only the exact owned alias during rollback/uninstall.\n- Validates Sunshine `LocalIP` locally before enabling the identity and still runs the existing real Android TCP + UDP probes before READY.\n- If live Android validation of the host-only identity fails, the session cleans up and retries the proven 3.1.x physical-address path instead of stranding the user.\n- This remains a release candidate until the host-only path is validated on real Android hardware.\n\n",
)
