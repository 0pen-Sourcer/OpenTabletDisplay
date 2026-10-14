//go:build !windows

package bridge

import (
	"fmt"
	"os"
)

func VerifyProcessOwner(pid int, expectedPath string) error {
	if os.Getenv("FSDL_ALLOW_NONWINDOWS") == "1" {
		return nil
	}
	return fmt.Errorf("process verification is Windows-only")
}
