//go:build !windows

package bridge

import "context"

func verifyPlatformToolsSignature(ctx context.Context, r Runner, adbPath string) error { return nil }
