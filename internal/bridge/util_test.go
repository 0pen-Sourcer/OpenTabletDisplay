package bridge

import (
	"context"
	"fmt"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestParseADBDevices(t *testing.T) {
	got := ParseADBDevices("List of devices attached\nABC\tdevice\nXYZ\tunauthorized\n")
	if got["ABC"] != "device" || got["XYZ"] != "unauthorized" {
		t.Fatalf("bad parse: %#v", got)
	}
}

func TestSingleAuthorizedDeviceRejectsMultiple(t *testing.T) {
	_, err := SingleAuthorizedDevice(map[string]string{"A": "device", "B": "device"})
	if err == nil {
		t.Fatal("expected error")
	}
}

func TestDownloadPinnedRejectsWrongHash(t *testing.T) {
	ts := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { _, _ = w.Write([]byte("hello")) }))
	defer ts.Close()
	p := filepath.Join(t.TempDir(), "x")
	err := DownloadPinned(context.Background(), ts.Client(), ts.URL, strings.Repeat("0", 64), p)
	if err == nil {
		t.Fatal("expected mismatch")
	}
	if _, statErr := os.Stat(p); !os.IsNotExist(statErr) {
		t.Fatalf("bad file should not survive")
	}
}

func TestWaitForTextAnyFindsSecondLog(t *testing.T) {
	d := t.TempDir()
	a := filepath.Join(d, "a.log")
	b := filepath.Join(d, "b.log")
	if err := os.WriteFile(a, []byte("nothing\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(b, []byte("TunnelServer: Client #7 connected\n"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := WaitForTextAny([]string{a, b}, "TunnelServer: Client #", 250*time.Millisecond); err != nil {
		t.Fatal(err)
	}
}

func TestFreeLoopbackTCPPort(t *testing.T) {
	p, err := freeLoopbackTCPPort()
	if err != nil {
		t.Fatal(err)
	}
	if p < 1024 || p > 65535 {
		t.Fatalf("unexpected port %d", p)
	}
	ln, err := net.Listen("tcp4", fmt.Sprintf("127.0.0.1:%d", p))
	if err != nil {
		t.Fatalf("port not reusable after allocator closed it: %v", err)
	}
	_ = ln.Close()
}

func FuzzParseADBDevices(f *testing.F) {
	f.Add("List of devices attached\nABC\tdevice\nXYZ\tunauthorized\n")
	f.Add("")
	f.Add("* daemon started successfully *\nList of devices attached\n")
	f.Fuzz(func(t *testing.T, in string) {
		m := ParseADBDevices(in)
		for serial, state := range m {
			if strings.TrimSpace(serial) == "" || strings.TrimSpace(state) == "" {
				t.Fatalf("empty parsed field from %q: %#v", in, m)
			}
		}
		_, _ = SingleAuthorizedDevice(m)
	})
}
