package bridge

import (
	"archive/zip"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

type Runner interface {
	Run(ctx context.Context, name string, args ...string) (stdout string, stderr string, err error)
	Start(name string, args []string, stdoutPath, stderrPath string) (pid int, err error)
}

type OSRunner struct{}

func (OSRunner) Run(ctx context.Context, name string, args ...string) (string, string, error) {
	cmd := exec.CommandContext(ctx, name, args...)
	var out, er strings.Builder
	cmd.Stdout = &out
	cmd.Stderr = &er
	err := cmd.Run()
	return out.String(), er.String(), err
}

func (OSRunner) Start(name string, args []string, stdoutPath, stderrPath string) (int, error) {
	if err := os.MkdirAll(filepath.Dir(stdoutPath), 0o755); err != nil {
		return 0, err
	}
	out, err := os.OpenFile(stdoutPath, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o644)
	if err != nil {
		return 0, err
	}
	defer out.Close()
	er, err := os.OpenFile(stderrPath, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o644)
	if err != nil {
		return 0, err
	}
	defer er.Close()
	cmd := exec.Command(name, args...)
	cmd.Stdout = out
	cmd.Stderr = er
	prepareDetached(cmd)
	if err := cmd.Start(); err != nil {
		return 0, err
	}
	pid := cmd.Process.Pid
	_ = cmd.Process.Release()
	return pid, nil
}

func SHA256File(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer f.Close()
	h := sha256.New()
	if _, err := io.Copy(h, f); err != nil {
		return "", err
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}

func DownloadPinned(ctx context.Context, client *http.Client, url, expectedSHA, dst string) error {
	if err := os.MkdirAll(filepath.Dir(dst), 0o755); err != nil {
		return err
	}
	tmp := dst + ".part"
	_ = os.Remove(tmp)
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return err
	}
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("download returned HTTP %d", resp.StatusCode)
	}
	f, err := os.Create(tmp)
	if err != nil {
		return err
	}
	_, copyErr := io.Copy(f, resp.Body)
	closeErr := f.Close()
	if copyErr != nil {
		_ = os.Remove(tmp)
		return copyErr
	}
	if closeErr != nil {
		_ = os.Remove(tmp)
		return closeErr
	}
	got, err := SHA256File(tmp)
	if err != nil {
		_ = os.Remove(tmp)
		return err
	}
	if !strings.EqualFold(got, expectedSHA) {
		_ = os.Remove(tmp)
		return fmt.Errorf("SHA-256 mismatch: expected %s, got %s", expectedSHA, got)
	}
	_ = os.Remove(dst)
	return os.Rename(tmp, dst)
}

func ExtractZipSafe(zipPath, dst string) error {
	zr, err := zip.OpenReader(zipPath)
	if err != nil {
		return err
	}
	defer zr.Close()
	base, err := filepath.Abs(dst)
	if err != nil {
		return err
	}
	if err := os.MkdirAll(base, 0o755); err != nil {
		return err
	}
	for _, zf := range zr.File {
		if zf.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("refusing symlink in zip: %q", zf.Name)
		}
		clean := filepath.Clean(zf.Name)
		if clean == "." || filepath.IsAbs(clean) || clean == ".." || strings.HasPrefix(clean, ".."+string(os.PathSeparator)) {
			return fmt.Errorf("unsafe zip path: %q", zf.Name)
		}
		target := filepath.Join(base, clean)
		absTarget, err := filepath.Abs(target)
		if err != nil {
			return err
		}
		if absTarget != base && !strings.HasPrefix(absTarget, base+string(os.PathSeparator)) {
			return fmt.Errorf("zip path escapes destination: %q", zf.Name)
		}
		if zf.FileInfo().IsDir() {
			if err := os.MkdirAll(absTarget, 0o755); err != nil {
				return err
			}
			continue
		}
		if err := os.MkdirAll(filepath.Dir(absTarget), 0o755); err != nil {
			return err
		}
		rc, err := zf.Open()
		if err != nil {
			return err
		}
		out, err := os.OpenFile(absTarget, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o755)
		if err != nil {
			rc.Close()
			return err
		}
		_, cpErr := io.Copy(out, rc)
		rc.Close()
		out.Close()
		if cpErr != nil {
			return cpErr
		}
	}
	return nil
}

func WaitForText(path, needle string, timeout time.Duration) error {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		b, _ := os.ReadFile(path)
		if strings.Contains(string(b), needle) {
			return nil
		}
		time.Sleep(250 * time.Millisecond)
	}
	return fmt.Errorf("timed out waiting for %q in %s", needle, path)
}

func WaitForTextAny(paths []string, needle string, timeout time.Duration) error {
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		for _, path := range paths {
			b, _ := os.ReadFile(path)
			if strings.Contains(string(b), needle) {
				return nil
			}
		}
		time.Sleep(250 * time.Millisecond)
	}
	return fmt.Errorf("timed out waiting for %q in %v", needle, paths)
}

func HTTP200(ctx context.Context, client *http.Client, url string) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return err
	}
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return fmt.Errorf("HTTP %d", resp.StatusCode)
	}
	return nil
}

func ParseADBDevices(out string) map[string]string {
	m := map[string]string{}
	for _, line := range strings.Split(out, "\n") {
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "List of devices") || strings.HasPrefix(line, "*") {
			continue
		}
		f := strings.Fields(line)
		if len(f) >= 2 {
			m[f[0]] = f[1]
		}
	}
	return m
}

func SingleAuthorizedDevice(devs map[string]string) (string, error) {
	var authorized []string
	for serial, state := range devs {
		if state == "device" {
			authorized = append(authorized, serial)
		}
	}
	if len(authorized) == 0 {
		return "", errors.New("no authorized Android device")
	}
	if len(authorized) > 1 {
		return "", fmt.Errorf("multiple authorized Android devices: %v", authorized)
	}
	return authorized[0], nil
}
