//go:build !windows

package bridge

import "os/exec"

func prepareHidden(cmd *exec.Cmd)   {}
func prepareDetached(cmd *exec.Cmd) {}
