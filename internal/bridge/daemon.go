package bridge

import (
	"context"
	"fmt"
	"os"
	"strings"
	"time"
)

// Daemon keeps the wired Android transport available without requiring a
// visible launcher window. The installer runs it from a per-user scheduled task.
func (a *App) Daemon(ctx context.Context) error {
	a.Out("FSDL USB agent %s", Version)
	a.Out("Waiting for an authorized Android display over USB...")

	adb, _, err := a.ensureDependencies(ctx)
	if err != nil {
		return fmt.Errorf("agent dependencies: %w", err)
	}

	var lastSerial string
	lastStatus := ""
	ticker := time.NewTicker(3 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			_ = a.Stop(context.Background())
			return nil
		case <-ticker.C:
		}

		// Android's stock USB tethering creates a competing RNDIS topology. FSDL
		// deliberately waits instead of silently fighting or rewriting that network.
		if active, e := normalUSBTetherActive(ctx, a.Runner); e == nil && active {
			if _, e2 := a.loadState(); e2 == nil {
				_ = a.Stop(context.Background())
			}
			if lastStatus != "tether" {
				a.Out("Android USB tethering is ON; FSDL is paused until it is OFF.")
				lastStatus = "tether"
			}
			lastSerial = ""
			continue
		}

		out, _, _ := a.Runner.Run(ctx, adb, "devices")
		devs := ParseADBDevices(out)
		serial, selectErr := SelectAuthorizedDevice(devs, a.enrolledSerial())
		if selectErr != nil {
			if lastSerial != "" {
				_ = a.Stop(context.Background())
			}
			status := "waiting:" + selectErr.Error()
			if status != lastStatus {
				a.Out("Waiting: %v", selectErr)
				lastStatus = status
			}
			lastSerial = ""
			continue
		}

		if a.activeStateHealthy(ctx, serial) {
			if lastStatus != "ready" {
				a.Out("USB bridge healthy for %s", serial)
				lastStatus = "ready"
			}
			lastSerial = serial
			continue
		}

		if lastStatus != "starting:"+serial {
			a.Out("Authorized Android device %s detected; establishing FSDL transport...", serial)
			lastStatus = "starting:" + serial
		}
		runCtx, cancel := context.WithTimeout(ctx, 3*time.Minute)
		err = a.Start(runCtx)
		cancel()
		if err != nil {
			a.Out("Agent start failed: %v", err)
			select {
			case <-ctx.Done():
				_ = a.Stop(context.Background())
				return nil
			case <-time.After(8 * time.Second):
			}
			lastStatus = "failed:" + err.Error()
		} else {
			a.Out("FSDL USB transport READY in background.")
			lastStatus = "ready"
		}
		lastSerial = serial
	}
}

func (a *App) activeStateHealthy(ctx context.Context, serial string) bool {
	st, err := a.loadState()
	if err != nil || st.Serial != serial || st.RelayPID <= 0 || st.GnirehtetPath == "" || st.ADBPath == "" || st.HostIP == "" {
		return false
	}
	if err := VerifyProcessOwner(st.RelayPID, st.GnirehtetPath); err != nil {
		return false
	}
	hostIP, err := detectHostIPv4(ctx, a.Runner)
	if err != nil || hostIP != st.HostIP {
		return false
	}
	// Verify the ADB reverse mapping still targets our randomized relay port.
	out, _, err := a.Runner.Run(ctx, st.ADBPath, "-s", serial, "reverse", "--list")
	if err != nil {
		return false
	}
	wantPort := fmt.Sprintf("tcp:%d", st.RelayPort)
	if !strings.Contains(out, "localabstract:gnirehtet") || !strings.Contains(out, wantPort) {
		return false
	}
	return true
}

func AgentLogPath(root string) string {
	return root + string(os.PathSeparator) + "runtime" + string(os.PathSeparator) + "agent.log"
}
