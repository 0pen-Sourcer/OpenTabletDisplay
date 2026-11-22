//go:build windows

// OpenTabletDisplay is a GUI subsystem launcher. The application interface is
// implemented with Windows' built-in WPF stack, so users don't need .NET SDK
// downloads, Electron, or any additional UI runtime.
package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"syscall"
	"unsafe"
)

var (
	user32     = syscall.NewLazyDLL("user32.dll")
	messageBox = user32.NewProc("MessageBoxW")
)

func showError(message string) {
	msg, err := syscall.UTF16PtrFromString(message)
	if err != nil {
		return
	}
	title, _ := syscall.UTF16PtrFromString("OpenTabletDisplay")
	messageBox.Call(0, uintptr(unsafe.Pointer(msg)), uintptr(unsafe.Pointer(title)), 0x10)
}

func main() {
	exe, err := os.Executable()
	if err != nil {
		showError("Unable to locate OpenTabletDisplay. Please re-extract the release archive.")
		return
	}
	root := filepath.Dir(exe)
	candidates := []string{
		filepath.Join(root, "installer", "desktop.ps1"),
		filepath.Join(root, "desktop.ps1"),
	}
	var script string
	for _, candidate := range candidates {
		if fi, e := os.Stat(candidate); e == nil && !fi.IsDir() {
			script = candidate
			break
		}
	}
	if script == "" {
		showError("The interface files are missing.\n\nDownload the packaged release ZIP (not GitHub's Source code ZIP) and extract all files before opening OpenTabletDisplay.exe.")
		return
	}
	systemRoot := os.Getenv("SystemRoot")
	if systemRoot == "" {
		systemRoot = `C:\Windows`
	}
	powershell := filepath.Join(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
	command := exec.Command(powershell, "-NoProfile", "-STA", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script)
	command.Dir = root
	command.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	if err := command.Start(); err != nil {
		showError(fmt.Sprintf("Could not open the Windows interface:\n%s\n\nIf Windows PowerShell is disabled by policy, contact your administrator.", err))
	}
}
