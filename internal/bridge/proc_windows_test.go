//go:build windows

package bridge

import (
	"context"
	"os/exec"
	"strings"
	"testing"
	"time"
)

// Regression: the GUI-subsystem USB agent polls Windows networking every
// three seconds. Any visible console attached to those probes causes the
// PowerShell flash that was reported on real Windows hardware.
func TestWindowsHiddenProbePowerShell(t *testing.T) {
	cmd := exec.Command("powershell.exe", "-NoProfile", "-NonInteractive")
	prepareHidden(cmd)
	if cmd.SysProcAttr == nil || !cmd.SysProcAttr.HideWindow ||
		cmd.SysProcAttr.CreationFlags&createNoWindow == 0 {
		t.Fatal("PowerShell probe must be launched without a console window")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 35*time.Second)
	defer cancel()
	// Verify the actual OSRunner.Run path rather than just the helper.
	script := `Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public class FSDLConsoleCheck { [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow(); }'; if ([FSDLConsoleCheck]::GetConsoleWindow() -ne [IntPtr]::Zero) { Write-Error 'Probe inherited or allocated a console'; exit 17 }; Write-Output 'NO_CONSOLE'`
	out, stderr, err := (OSRunner{}).Run(ctx, "powershell.exe",
		"-NoProfile", "-NonInteractive", "-Command", script)
	if err != nil {
		t.Fatalf("PowerShell probe launched a console or failed: %v (stderr=%q, stdout=%q)", err, stderr, out)
	}
	if !strings.Contains(out, "NO_CONSOLE") {
		t.Fatalf("Expected no-console proof from PowerShell; stdout=%q stderr=%q", out, stderr)
	}
}
