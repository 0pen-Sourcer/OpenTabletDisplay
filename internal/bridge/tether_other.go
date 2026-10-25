//go:build !windows

package bridge

import (
	"context"
	"os"
)

func normalUSBTetherActive(ctx context.Context, r Runner) (bool, error) { return false, nil }

func detectHostIPv4(ctx context.Context, r Runner, root string) (string, error) {
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
}

func cleanupLegacyFSDLRelays(ctx context.Context, r Runner) error { return nil }
