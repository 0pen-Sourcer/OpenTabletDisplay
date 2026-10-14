package bridge

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

const remoteProbePath = "/data/local/tmp/fsdl-probe"

func probeFilenameForABI(abi string) string {
	switch strings.TrimSpace(abi) {
	case "arm64-v8a":
		return "fsdl-probe-android-arm64"
	default:
		return ""
	}
}

func (a *App) prepareAndroidProbe(ctx context.Context, adb, serial string) (string, func(), error) {
	out, er, err := a.Runner.Run(ctx, adb, "-s", serial, "shell", "getprop", "ro.product.cpu.abi")
	if err != nil {
		return "", func() {}, fmt.Errorf("query Android ABI: %v (%s)", err, strings.TrimSpace(er))
	}
	abi := strings.TrimSpace(out)
	filename := probeFilenameForABI(abi)
	if filename == "" {
		// Fallback for uncommon architectures/ROMs. Toybox nc is widespread but not
		// guaranteed, so the release ships a native probe for the hardware-validated ARM64 Android path and uses the ROM tool for other ABIs.
		_, er2, e2 := a.Runner.Run(ctx, adb, "-s", serial, "shell", "toybox", "nc", "--help")
		if e2 != nil && !strings.Contains(strings.ToLower(er2), "usage") {
			return "", func() {}, fmt.Errorf("unsupported Android ABI %q and toybox nc is unavailable", abi)
		}
		return "toybox-nc", func() {}, nil
	}

	local := filepath.Join(a.Root, "probes", filename)
	info, err := os.Stat(local)
	if err != nil {
		return "", func() {}, fmt.Errorf("release is missing %s for Android ABI %s: %w", filename, abi, err)
	}
	if info.IsDir() {
		return "", func() {}, fmt.Errorf("probe path is a directory: %s", local)
	}
	if _, er, err := a.Runner.Run(ctx, adb, "-s", serial, "push", local, remoteProbePath); err != nil {
		return "", func() {}, fmt.Errorf("push Android transport probe: %v (%s)", err, strings.TrimSpace(er))
	}
	if _, er, err := a.Runner.Run(ctx, adb, "-s", serial, "shell", "chmod", "700", remoteProbePath); err != nil {
		return "", func() {}, fmt.Errorf("chmod Android transport probe: %v (%s)", err, strings.TrimSpace(er))
	}
	cleanup := func() {
		_, _, _ = a.Runner.Run(context.Background(), adb, "-s", serial, "shell", "rm", "-f", remoteProbePath)
	}
	return remoteProbePath, cleanup, nil
}
