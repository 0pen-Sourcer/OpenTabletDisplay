package bridge

import (
	"archive/zip"
	"context"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

type fakeRunner struct {
	mu       sync.Mutex
	calls    []string
	serial   string
	relayLog string
}

func (f *fakeRunner) Run(ctx context.Context, name string, args ...string) (string, string, error) {
	f.mu.Lock()
	f.calls = append(f.calls, name+" "+strings.Join(args, " "))
	f.mu.Unlock()
	joined := strings.Join(args, " ")
	if strings.HasSuffix(strings.ToLower(name), "adb.exe") {
		switch {
		case joined == "devices":
			return "List of devices attached\n" + f.serial + "\tdevice\n", "", nil
		case strings.Contains(joined, "getprop ro.product.cpu.abi"):
			return "arm64-v8a\n", "", nil
		case strings.Contains(joined, remoteProbePath+" tcp "):
			return "HTTP/1.0 200 OK\n\n<root status_code=\"200\"><LocalIP>192.168.50.23</LocalIP><HttpsPort>47984</HttpsPort><state>SUNSHINE_SERVER_FREE</state></root>", "", nil
		case strings.Contains(joined, remoteProbePath+" udp "):
			fields := strings.Fields(joined)
			if len(fields) < 2 {
				return "", "", fmt.Errorf("bad embedded udp probe command")
			}
			var port int
			if _, e := fmt.Sscanf(fields[len(fields)-2], "%d", &port); e != nil {
				return "", "", e
			}
			token := fields[len(fields)-1]
			c, e := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.ParseIP("127.0.0.1"), Port: port})
			if e != nil {
				return "", "", e
			}
			_, e = c.Write([]byte(token))
			_ = c.Close()
			return "sent\n", "", e
		case strings.Contains(joined, "toybox nc") && strings.Contains(joined, "47989"):
			return "HTTP/1.0 200 OK\r\n\r\n<root status_code=\"200\"><LocalIP>192.168.50.23</LocalIP><HttpsPort>47984</HttpsPort><state>SUNSHINE_SERVER_FREE</state></root>", "", nil
		default:
			return "", "", nil
		}
	}
	return "", "", nil
}

func (f *fakeRunner) Start(name string, args []string, stdoutPath, stderrPath string) (int, error) {
	f.mu.Lock()
	f.calls = append(f.calls, name+" "+strings.Join(args, " "))
	f.mu.Unlock()
	f.relayLog = stdoutPath
	if err := os.MkdirAll(filepath.Dir(stdoutPath), 0o755); err != nil {
		return 0, err
	}
	if err := os.WriteFile(stdoutPath, []byte("INFO TunnelServer: Client #0 connected\n"), 0o644); err != nil {
		return 0, err
	}
	_ = os.WriteFile(stderrPath, nil, 0o644)
	return 4242, nil
}

func writeZip(path string, files map[string]string) error {
	f, err := os.Create(path)
	if err != nil {
		return err
	}
	zw := zip.NewWriter(f)
	for name, content := range files {
		w, err := zw.Create(name)
		if err != nil {
			return err
		}
		if _, err := io.WriteString(w, content); err != nil {
			return err
		}
	}
	if err := zw.Close(); err != nil {
		return err
	}
	return f.Close()
}

func RunSelfTest() error {
	dir, err := os.MkdirTemp("", "fsdl-selftest-")
	if err != nil {
		return err
	}
	defer os.RemoveAll(dir)

	// 1) ADB parser invariants.
	devs := ParseADBDevices("List of devices attached\nABC\tdevice\n")
	if s, err := SingleAuthorizedDevice(devs); err != nil || s != "ABC" {
		return fmt.Errorf("ADB parser")
	}
	if _, err := SingleAuthorizedDevice(ParseADBDevices("List of devices attached\nA\tdevice\nB\tdevice\n")); err == nil {
		return fmt.Errorf("multiple-device guard missing")
	}

	// 2) Safe zip extraction and zip-slip rejection.
	goodZip := filepath.Join(dir, "good.zip")
	if err := writeZip(goodZip, map[string]string{"platform-tools/adb.exe": "fake"}); err != nil {
		return err
	}
	if err := ExtractZipSafe(goodZip, filepath.Join(dir, "good")); err != nil {
		return err
	}
	badZip := filepath.Join(dir, "bad.zip")
	if err := writeZip(badZip, map[string]string{"../escape.txt": "bad"}); err != nil {
		return err
	}
	if err := ExtractZipSafe(badZip, filepath.Join(dir, "bad")); err == nil {
		return fmt.Errorf("zip-slip guard failed")
	}

	// 3) Download+hash verification against a local HTTPS-independent server.
	payload := []byte("known-payload")
	shaPath := filepath.Join(dir, "expected")
	if err := os.WriteFile(shaPath, payload, 0o644); err != nil {
		return err
	}
	expected, _ := SHA256File(shaPath)
	ts := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { _, _ = w.Write(payload) }))
	defer ts.Close()
	if err := DownloadPinned(context.Background(), ts.Client(), ts.URL, expected, filepath.Join(dir, "download.bin")); err != nil {
		return err
	}
	if err := DownloadPinned(context.Background(), ts.Client(), ts.URL, strings.Repeat("0", 64), filepath.Join(dir, "download-bad.bin")); err == nil {
		return fmt.Errorf("hash mismatch was not rejected")
	}

	// 4) Sunshine HTTP health check behavior.
	sun := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/serverinfo" {
			http.NotFound(w, r)
			return
		}
		w.WriteHeader(200)
		_, _ = w.Write([]byte("<root status_code=\"200\"><LocalIP>192.168.50.23</LocalIP><HttpsPort>47984</HttpsPort><state>SUNSHINE_SERVER_FREE</state></root>"))
	}))
	defer sun.Close()
	if err := HTTP200(context.Background(), sun.Client(), sun.URL+"/serverinfo"); err != nil {
		return err
	}

	// 5) Relay-log end-to-end state detection.
	logPath := filepath.Join(dir, "relay.log")
	go func() {
		time.Sleep(100 * time.Millisecond)
		_ = os.WriteFile(logPath, []byte("INFO TunnelServer: Client #0 connected\n"), 0o644)
	}()
	if err := WaitForText(logPath, "TunnelServer: Client #", 2*time.Second); err != nil {
		return err
	}

	// 6) Full orchestration simulation: Sunshine -> dependencies present -> ADB -> stale reset ->
	// relay connect -> tablet-side TCP probe -> tablet-side UDP probe -> state persisted.
	simRoot := filepath.Join(dir, "integration")
	adbPath := filepath.Join(simRoot, "runtime", "tools", "platform-tools", "adb.exe")
	gniPath := filepath.Join(simRoot, "runtime", "tools", "gnirehtet", "gnirehtet.exe")
	if err := os.MkdirAll(filepath.Dir(adbPath), 0o755); err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(gniPath), 0o755); err != nil {
		return err
	}
	if err := os.WriteFile(adbPath, []byte("fake"), 0o755); err != nil {
		return err
	}
	if err := os.WriteFile(gniPath, []byte("fake"), 0o755); err != nil {
		return err
	}
	gniAPKPath := filepath.Join(filepath.Dir(gniPath), "gnirehtet.apk")
	if err := os.WriteFile(gniAPKPath, []byte("fake-apk"), 0o644); err != nil {
		return err
	}
	probeDir := filepath.Join(simRoot, "probes")
	if err := os.MkdirAll(probeDir, 0o755); err != nil {
		return err
	}
	if err := os.WriteFile(filepath.Join(probeDir, "fsdl-probe-android-arm64"), []byte("fake-probe"), 0o755); err != nil {
		return err
	}
	fr := &fakeRunner{serial: "ANDROID_TEST"}
	app := New(simRoot)
	app.Runner = fr
	app.HTTP = sun.Client()
	app.SunshineURL = sun.URL + "/serverinfo"
	app.Out = func(string, ...any) {}
	oldAllow := os.Getenv("FSDL_ALLOW_NONWINDOWS")
	_ = os.Setenv("FSDL_ALLOW_NONWINDOWS", "1")
	defer os.Setenv("FSDL_ALLOW_NONWINDOWS", oldAllow)
	if err := app.Start(context.Background()); err != nil {
		return fmt.Errorf("integration simulation: %w", err)
	}
	st, err := app.loadState()
	if err != nil {
		return fmt.Errorf("integration state: %w", err)
	}
	if st.Serial != "ANDROID_TEST" || st.RelayPID != 4242 || st.HostIP != "192.168.50.23" {
		return fmt.Errorf("integration state wrong: %#v", st)
	}
	enrolled, err := app.loadEnrollment()
	if err != nil || enrolled.Serial != "ANDROID_TEST" {
		return fmt.Errorf("integration enrollment wrong: %#v err=%v", enrolled, err)
	}
	if got := os.Getenv("GNIREHTET_APK"); got != gniAPKPath {
		return fmt.Errorf("integration GNIREHTET_APK=%q, want %q", got, gniAPKPath)
	}
	fr.mu.Lock()
	calls := strings.Join(fr.calls, "\n")
	fr.mu.Unlock()
	stopIdx := strings.Index(calls, "gnirehtet.exe stop ANDROID_TEST")
	startIdx := strings.Index(calls, "gnirehtet.exe start ANDROID_TEST -p ")
	if stopIdx < 0 || startIdx < 0 || stopIdx > startIdx || !strings.Contains(calls[startIdx:], " -r 192.168.50.23/32") {
		return fmt.Errorf("stale-client reset / scoped route ordering failed:\n%s", calls)
	}
	relayIdx := strings.Index(calls, "gnirehtet.exe relay -p ")
	if relayIdx < 0 || relayIdx > startIdx {
		return fmt.Errorf("randomized relay was not started before client:\n%s", calls)
	}

	return nil
}
