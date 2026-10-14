//go:build !windows

package bridge

import (
	"context"
	"os"
)

func normalUSBTetherActive(ctx context.Context, r Runner) (bool, error) { return false, nil }

func detectHostIPv4(ctx context.Context, r Runner) (string, error) {
	if v := os.Getenv("FSDL_TEST_HOST_IP"); v != "" {
		return v, nil
	}
	return "192.168.50.23", nil
}

func cleanupLegacyFSDLRelays(ctx context.Context, r Runner) error { return nil }
