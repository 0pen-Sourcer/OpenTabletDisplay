//go:build !windows

package bridge

import (
	"fmt"
	"os"
)

func KillVerifiedProcess(pid int, expectedPath string) error {
	if os.Getenv("FSDL_SELFTEST_ALLOW_KILL") != "1" {
		return fmt.Errorf("process kill verification is Windows-only outside self-test")
	}
	p, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return p.Kill()
}
