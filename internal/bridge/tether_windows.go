//go:build windows

package bridge

import (
	"context"
	"fmt"
	"net"
	"os"
	"strings"
)

func normalUSBTetherActive(ctx context.Context, r Runner) (bool, error) {
	script := `$x = Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' -and $_.InterfaceDescription -match 'Remote NDIS based Internet Sharing Device' }; if ($x) { 'ACTIVE' } else { 'OFF' }`
	out, er, err := r.Run(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script)
	if err != nil {
		return false, fmt.Errorf("detect Android USB tether adapter: %v (%s)", err, strings.TrimSpace(er))
	}
	return strings.Contains(out, "ACTIVE"), nil
}

// detectHostIPv4 prefers the stable OpenTabletDisplay-owned loopback /32 identity.
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
func detectPhysicalHostIPv4(ctx context.Context, r Runner) (string, error) {
	script := `
$ErrorActionPreference='Stop'
$routes = Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
  Where-Object { $_.NextHop -and $_.NextHop -ne '0.0.0.0' } |
  Sort-Object RouteMetric
$rows = @()
foreach ($route in $routes) {
  $ad = Get-NetAdapter -InterfaceIndex $route.InterfaceIndex -ErrorAction SilentlyContinue
  if (-not $ad -or $ad.Status -ne 'Up') { continue }
  $label = (($ad.Name + ' ' + $ad.InterfaceDescription) -as [string])
  if ($label -match 'Remote NDIS|VirtualBox|Hyper-V|VMware|Loopback|TAP|Wintun|WireGuard') { continue }
  $ips = Get-NetIPAddress -InterfaceIndex $route.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notmatch '^169\\.254\\.' -and $_.IPAddress -ne '127.0.0.1' }
  foreach ($ip in $ips) {
    $rows += [PSCustomObject]@{
      IP=$ip.IPAddress
      Hardware=[bool]$ad.HardwareInterface
      RouteMetric=[int]$route.RouteMetric
      InterfaceMetric=[int](Get-NetIPInterface -InterfaceIndex $route.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue).InterfaceMetric
    }
  }
}
$pick = $rows | Sort-Object @{Expression='Hardware';Descending=$true}, @{Expression={ $_.RouteMetric + $_.InterfaceMetric };Ascending=$true} | Select-Object -First 1
if (-not $pick) { exit 3 }
$pick.IP
`
	out, er, err := r.Run(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script)
	if err != nil {
		return "", fmt.Errorf("select physical Windows host IPv4: %v (%s)", err, strings.TrimSpace(er))
	}
	ip := strings.TrimSpace(out)
	parsed := net.ParseIP(ip)
	if parsed == nil || parsed.To4() == nil || parsed.IsLoopback() || parsed.IsUnspecified() || parsed.IsLinkLocalUnicast() {
		return "", fmt.Errorf("selected invalid Windows host IPv4 %q", ip)
	}
	return ip, nil
}

// Older test builds could leave a detached gnirehtet relay alive because they lived in separate
// extracted folder/state file. Kill only relays whose executable path is unmistakably
// owned by an older FSDL USB Bridge r2/r3/r4 folder. No generic gnirehtet processes are touched.
func cleanupLegacyFSDLRelays(ctx context.Context, r Runner) error {
	script := `
$ErrorActionPreference='SilentlyContinue'
$procs = Get-CimInstance Win32_Process -Filter "Name='gnirehtet.exe'"
foreach ($p in $procs) {
  $path = [string]$p.ExecutablePath
  $cmd = [string]$p.CommandLine
  if ($path -match 'FSDL_USB_Bridge_3\.0\.3-r[234]_AUDITED' -and $path -match 'runtime\\tools\\gnirehtet\\gnirehtet\.exe$' -and $cmd -match '\brelay\b') {
    Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
  }
}
`
	_, er, err := r.Run(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script)
	if err != nil {
		return fmt.Errorf("clean previous FSDL relay: %v (%s)", err, strings.TrimSpace(er))
	}
	return nil
}
