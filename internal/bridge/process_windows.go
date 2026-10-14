//go:build windows

package bridge

import (
	"fmt"
	"os"
	"path/filepath"
	"syscall"
	"unsafe"
)

var (
	kernel32                       = syscall.NewLazyDLL("kernel32.dll")
	procOpenProcess                = kernel32.NewProc("OpenProcess")
	procQueryFullProcessImageNameW = kernel32.NewProc("QueryFullProcessImageNameW")
	procCloseHandle                = kernel32.NewProc("CloseHandle")
)

const processQueryLimitedInformation = 0x1000

func processPath(pid int) (string, error) {
	h, _, e := procOpenProcess.Call(processQueryLimitedInformation, 0, uintptr(pid))
	if h == 0 {
		return "", e
	}
	defer procCloseHandle.Call(h)
	buf := make([]uint16, 32768)
	sz := uint32(len(buf))
	r, _, e := procQueryFullProcessImageNameW.Call(h, 0, uintptr(unsafe.Pointer(&buf[0])), uintptr(unsafe.Pointer(&sz)))
	if r == 0 {
		return "", e
	}
	return syscall.UTF16ToString(buf[:sz]), nil
}

func KillVerifiedProcess(pid int, expectedPath string) error {
	actual, err := processPath(pid)
	if err != nil {
		return err
	}
	a, _ := filepath.Abs(actual)
	e, _ := filepath.Abs(expectedPath)
	if !samePath(a, e) {
		return fmt.Errorf("refusing to kill PID %d: executable is %q, expected %q", pid, actual, expectedPath)
	}
	p, err := os.FindProcess(pid)
	if err != nil {
		return err
	}
	return p.Kill()
}

func samePath(a, b string) bool {
	return filepath.Clean(a) == filepath.Clean(b) || stringsEqualFold(filepath.Clean(a), filepath.Clean(b))
}

func stringsEqualFold(a, b string) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		ca, cb := a[i], b[i]
		if ca >= 'A' && ca <= 'Z' {
			ca += 'a' - 'A'
		}
		if cb >= 'A' && cb <= 'Z' {
			cb += 'a' - 'A'
		}
		if ca != cb {
			return false
		}
	}
	return true
}
