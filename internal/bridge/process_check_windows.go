//go:build windows

package bridge

import (
	"fmt"
	"path/filepath"
)

func VerifyProcessOwner(pid int, expectedPath string) error {
	actual, err := processPath(pid)
	if err != nil {
		return fmt.Errorf("PID %d is not running or cannot be inspected: %w", pid, err)
	}
	a, _ := filepath.Abs(actual)
	e, _ := filepath.Abs(expectedPath)
	if !samePath(a, e) {
		return fmt.Errorf("PID %d executable is %q, expected %q", pid, actual, expectedPath)
	}
	return nil
}
