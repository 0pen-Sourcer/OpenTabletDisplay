//go:build windows

package bridge

import (
	"os/exec"
	"syscall"
)

// CREATE_NO_WINDOW prevents a console from being allocated for child CLI
// probes launched by the background GUI agent (including powershell.exe).
const createNoWindow = 0x08000000

func prepareHidden(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{
		HideWindow:    true,
		CreationFlags: createNoWindow,
	}
}

func prepareDetached(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{
		HideWindow:    true,
		CreationFlags: syscall.CREATE_NEW_PROCESS_GROUP,
	}
}
