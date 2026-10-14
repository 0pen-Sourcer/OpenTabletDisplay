//go:build !windows

package bridge

import "os/exec"

func prepareDetached(cmd *exec.Cmd) {}
