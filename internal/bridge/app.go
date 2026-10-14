package bridge

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"encoding/xml"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"
)

type State struct {
	HostIP        string    `json:"host_ip"`
	Serial        string    `json:"serial"`
	RelayPID      int       `json:"relay_pid"`
	RelayPort     int       `json:"relay_port"`
	GnirehtetPath string    `json:"gnirehtet_path"`
	ADBPath       string    `json:"adb_path"`
	StartedAt     time.Time `json:"started_at"`
}

type App struct {
	Root        string
	Runner      Runner
	HTTP        *http.Client
	SunshineURL string
	Out         func(string, ...any)
}

func New(root string) *App {
	return &App{
		Root:        root,
		Runner:      OSRunner{},
		HTTP:        &http.Client{Timeout: 12 * time.Second},
		SunshineURL: SunshineServerInfoURL,
		Out:         func(f string, a ...any) { fmt.Printf(f+"\n", a...) },
	}
}

func (a *App) paths() (tools, logs, statePath, adb, gni string) {
	tools = filepath.Join(a.Root, "runtime", "tools")
	logs = filepath.Join(a.Root, "runtime", "logs")
	statePath = filepath.Join(a.Root, "runtime", "state.json")
	adb = filepath.Join(tools, "platform-tools", "adb.exe")
	gni = filepath.Join(tools, "gnirehtet", "gnirehtet.exe")
	return
}

func (a *App) ensureDependencies(ctx context.Context) (adb, gni string, err error) {
	tools, _, _, adb, gni := a.paths()
	if err = os.MkdirAll(tools, 0o755); err != nil {
		return
	}
	adbZip := filepath.Join(tools, "platform-tools_r"+AdbVersion+"-win.zip")
	if _, e := os.Stat(adb); e != nil {
		a.Out("Downloading pinned Android Platform-Tools %s from Google...", AdbVersion)
		if err = DownloadPinned(ctx, a.HTTP, AdbURL, AdbSHA256, adbZip); err != nil {
			return
		}
		_ = os.RemoveAll(filepath.Join(tools, "platform-tools"))
		if err = ExtractZipSafe(adbZip, tools); err != nil {
			return
		}
		_ = os.Remove(adbZip)
	}
	if _, e := os.Stat(adb); e != nil {
		err = fmt.Errorf("adb.exe not found after extraction")
		return
	}
	if err = verifyPlatformToolsSignature(ctx, a.Runner, adb); err != nil {
		return
	}

	gniZip := filepath.Join(tools, "gnirehtet-rust-win64-v"+GnirehtetVersion+".zip")
	if _, e := os.Stat(gni); e != nil {
		a.Out("Downloading pinned gnirehtet %s from Genymobile...", GnirehtetVersion)
		if err = DownloadPinned(ctx, a.HTTP, GnirehtetURL, GnirehtetSHA256, gniZip); err != nil {
			return
		}
		_ = os.RemoveAll(filepath.Join(tools, "gnirehtet"))
		tmp := filepath.Join(tools, "_gnirehtet_unpack")
		_ = os.RemoveAll(tmp)
		if err = ExtractZipSafe(gniZip, tmp); err != nil {
			return
		}
		_ = os.Remove(gniZip)
		// Upstream ZIP contains a single top-level gnirehtet-rust-win64 directory.
		entries, e := os.ReadDir(tmp)
		if e != nil {
			err = e
			return
		}
		var src string
		for _, ent := range entries {
			if ent.IsDir() {
				candidate := filepath.Join(tmp, ent.Name())
				if _, e2 := os.Stat(filepath.Join(candidate, "gnirehtet.exe")); e2 == nil {
					src = candidate
					break
				}
			}
		}
		if src == "" {
			if _, e2 := os.Stat(filepath.Join(tmp, "gnirehtet.exe")); e2 == nil {
				src = tmp
			}
		}
		if src == "" {
			err = fmt.Errorf("gnirehtet.exe not found in upstream archive")
			return
		}
		if err = copyDir(src, filepath.Join(tools, "gnirehtet")); err != nil {
			return
		}
		_ = os.RemoveAll(tmp)
	}
	if _, e := os.Stat(gni); e != nil {
		err = fmt.Errorf("gnirehtet.exe not found after extraction")
		return
	}
	return
}

func copyDir(src, dst string) error {
	if err := os.MkdirAll(dst, 0o755); err != nil {
		return err
	}
	ents, err := os.ReadDir(src)
	if err != nil {
		return err
	}
	for _, ent := range ents {
		sp := filepath.Join(src, ent.Name())
		dp := filepath.Join(dst, ent.Name())
		if ent.IsDir() {
			if err := copyDir(sp, dp); err != nil {
				return err
			}
			continue
		}
		b, err := os.ReadFile(sp)
		if err != nil {
			return err
		}
		if err := os.WriteFile(dp, b, 0o755); err != nil {
			return err
		}
	}
	return nil
}

func (a *App) waitRelayConnected(logPaths []string, timeout time.Duration) error {
	return WaitForTextAny(logPaths, "TunnelServer: Client #", timeout)
}

func randomToken() string {
	b := make([]byte, 12)
	_, _ = rand.Read(b)
	return "FSDL_" + hex.EncodeToString(b)
}

func freeLoopbackTCPPort() (int, error) {
	ln, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		return 0, err
	}
	defer ln.Close()
	return ln.Addr().(*net.TCPAddr).Port, nil
}

func (a *App) adbShell(ctx context.Context, adb, serial, command string) (string, string, error) {
	return a.Runner.Run(ctx, adb, "-s", serial, "shell", "sh", "-c", command)
}

type sunshineServerInfo struct {
	LocalIP   string `xml:"LocalIP"`
	HTTPSPort string `xml:"HttpsPort"`
	State     string `xml:"state"`
}

func parseServerInfoXML(text string) (sunshineServerInfo, error) {
	idx := strings.Index(text, "<?xml")
	if idx < 0 {
		idx = strings.Index(text, "<root")
	}
	if idx < 0 {
		return sunshineServerInfo{}, fmt.Errorf("Sunshine XML root not found")
	}
	var info sunshineServerInfo
	if err := xml.Unmarshal([]byte(text[idx:]), &info); err != nil {
		return sunshineServerInfo{}, fmt.Errorf("parse Sunshine serverinfo XML: %w", err)
	}
	return info, nil
}

func validateServerInfoAddress(text, hostIP string) error {
	if !strings.Contains(text, "200") {
		return fmt.Errorf("Sunshine response was not HTTP 200: %s", trimForLog(text, 700))
	}
	info, err := parseServerInfoXML(text)
	if err != nil {
		return fmt.Errorf("%w; response: %s", err, trimForLog(text, 700))
	}
	if strings.TrimSpace(info.LocalIP) != hostIP {
		return fmt.Errorf("Sunshine advertised LocalIP %q, expected %q; this address would break Moonlight session setup", info.LocalIP, hostIP)
	}
	if strings.TrimSpace(info.HTTPSPort) == "" {
		return fmt.Errorf("Sunshine serverinfo did not include HttpsPort")
	}
	return nil
}

func (a *App) validateSunshineAtHostIP(ctx context.Context, hostIP string) error {
	url := "http://" + net.JoinHostPort(hostIP, "47989") + "/serverinfo"
	// The non-Windows self-test substitutes a local httptest server while still
	// validating the same advertised LocalIP semantics. Production Windows always
	// probes the selected host address directly.
	if os.Getenv("FSDL_ALLOW_NONWINDOWS") == "1" && a.SunshineURL != SunshineServerInfoURL {
		url = a.SunshineURL
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return err
	}
	resp, err := a.HTTP.Do(req)
	if err != nil {
		return fmt.Errorf("connect to Sunshine through selected Windows address %s: %w", hostIP, err)
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(io.LimitReader(resp.Body, 256*1024))
	if err != nil {
		return err
	}
	wire := fmt.Sprintf("HTTP %d\\n%s", resp.StatusCode, body)
	return validateServerInfoAddress(wire, hostIP)
}

func (a *App) tcpSunshineProbe(ctx context.Context, adb, serial, probe, hostIP string) error {
	probeCtx, cancel := context.WithTimeout(ctx, 10*time.Second)
	defer cancel()
	if probe != "toybox-nc" {
		out, er, err := a.Runner.Run(probeCtx, adb, "-s", serial, "shell", probe, "tcp", hostIP, "47989")
		joined := out + "\n" + er
		if err != nil {
			return fmt.Errorf("Android TCP probe failed: %v (%s)", err, strings.TrimSpace(joined))
		}
		if err := validateServerInfoAddress(joined, hostIP); err != nil {
			return fmt.Errorf("Android Sunshine address-semantics probe failed: %w", err)
		}
		// Moonlight needs HTTPS pairing/control and RTSP setup after serverinfo.
		// The /32 route covers all ports, but probe the two critical TCP listeners
		// from the actual tablet before declaring READY.
		for _, port := range []string{"47984", "48010"} {
			_, perr, e := a.Runner.Run(probeCtx, adb, "-s", serial, "shell", probe, "connect", hostIP, port)
			if e != nil {
				return fmt.Errorf("Android could not reach Sunshine TCP %s on %s: %v (%s)", port, hostIP, e, strings.TrimSpace(perr))
			}
		}
		return nil
	}
	cmd := fmt.Sprintf(`printf 'GET /serverinfo HTTP/1.0\r\nHost: %s\r\nConnection: close\r\n\r\n' | toybox nc -w 5 %s 47989`, hostIP, hostIP)
	out, er, err := a.adbShell(probeCtx, adb, serial, cmd)
	joined := out + "\n" + er
	if err != nil {
		return fmt.Errorf("Android TCP probe command failed: %v (%s)", err, strings.TrimSpace(joined))
	}
	if err := validateServerInfoAddress(joined, hostIP); err != nil {
		return fmt.Errorf("Android netcat Sunshine address-semantics probe failed: %w", err)
	}
	return nil
}

func (a *App) udpProbe(ctx context.Context, adb, serial, probe, hostIP string) error {
	conn, err := net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4zero, Port: 0})
	if err != nil {
		return err
	}
	defer conn.Close()
	port := conn.LocalAddr().(*net.UDPAddr).Port
	token := randomToken()
	result := make(chan error, 1)
	go func() {
		_ = conn.SetReadDeadline(time.Now().Add(8 * time.Second))
		buf := make([]byte, 2048)
		n, _, e := conn.ReadFromUDP(buf)
		if e != nil {
			result <- e
			return
		}
		if !strings.Contains(string(buf[:n]), token) {
			result <- fmt.Errorf("UDP payload mismatch")
			return
		}
		result <- nil
	}()
	var runErr error
	var er string
	if probe != "toybox-nc" {
		_, er, runErr = a.Runner.Run(ctx, adb, "-s", serial, "shell", probe, "udp", hostIP, strconv.Itoa(port), token)
	} else {
		cmd := fmt.Sprintf(`printf '%s' | toybox nc -u -w 3 %s %d`, token, hostIP, port)
		_, er, runErr = a.adbShell(ctx, adb, serial, cmd)
	}
	if runErr != nil {
		return fmt.Errorf("Android UDP probe command failed: %v (%s)", runErr, strings.TrimSpace(er))
	}
	select {
	case e := <-result:
		return e
	case <-time.After(9 * time.Second):
		return fmt.Errorf("UDP probe timed out")
	case <-ctx.Done():
		return ctx.Err()
	}
}

func relayLogTail(paths ...string) string {
	var parts []string
	for _, p := range paths {
		b, _ := os.ReadFile(p)
		if len(b) == 0 {
			continue
		}
		text := string(b)
		if len(text) > 1600 {
			text = text[len(text)-1600:]
		}
		parts = append(parts, filepath.Base(p)+": "+strings.TrimSpace(text))
	}
	if len(parts) == 0 {
		return "(no relay log output)"
	}
	return strings.Join(parts, " | ")
}

func trimForLog(s string, n int) string {
	s = strings.TrimSpace(s)
	if len(s) <= n {
		return s
	}
	return s[:n] + "..."
}

func (a *App) saveState(st State) error {
	_, _, p, _, _ := a.paths()
	if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
		return err
	}
	b, _ := json.MarshalIndent(st, "", "  ")
	return os.WriteFile(p, b, 0o600)
}

func (a *App) loadState() (State, error) {
	_, _, p, _, _ := a.paths()
	b, err := os.ReadFile(p)
	if err != nil {
		return State{}, err
	}
	var st State
	if err := json.Unmarshal(b, &st); err != nil {
		return State{}, err
	}
	return st, nil
}

func configureGnirehtetEnvironment(adb, gni string) error {
	apk := filepath.Join(filepath.Dir(gni), "gnirehtet.apk")
	info, err := os.Stat(apk)
	if err != nil {
		return fmt.Errorf("gnirehtet APK is missing next to the pinned executable: %w", err)
	}
	if info.IsDir() {
		return fmt.Errorf("gnirehtet APK path is a directory: %s", apk)
	}
	if err := os.Setenv("ADB", adb); err != nil {
		return fmt.Errorf("set ADB path: %w", err)
	}
	// Upstream gnirehtet defaults to the relative path `gnirehtet.apk`.
	// FSDL launches the executable by absolute path from its own root, so leaving
	// this unset makes upstream call `adb install -r gnirehtet.apk` from the wrong
	// working directory. Upstream explicitly supports GNIREHTET_APK for this case.
	if err := os.Setenv("GNIREHTET_APK", apk); err != nil {
		return fmt.Errorf("set GNIREHTET_APK path: %w", err)
	}
	return nil
}

func (a *App) Start(ctx context.Context) error {
	if runtime.GOOS != "windows" && os.Getenv("FSDL_ALLOW_NONWINDOWS") != "1" {
		return fmt.Errorf("this release is for Windows; use --selftest for the sandbox test suite")
	}
	a.Out("FSDL USB Bridge %s", Version)

	// Remove only detached relay processes owned by known older FSDL bring-up folders.
	// This migrates early test builds without touching unrelated gnirehtet processes.
	if err := cleanupLegacyFSDLRelays(ctx, a.Runner); err != nil {
		return err
	}

	// A second launch must never create a second relay. Only stop a relay recorded
	// in our own state file, and only via verified executable-path ownership.
	if _, err := a.loadState(); err == nil {
		a.Out("Previous bridge state found; stopping the owned relay first...")
		if err := a.Stop(ctx); err != nil {
			return fmt.Errorf("stop previous bridge: %w", err)
		}
	}
	a.Out("[1/7] Sunshine control endpoint")
	if err := HTTP200(ctx, a.HTTP, a.SunshineURL); err != nil {
		return fmt.Errorf("Sunshine /serverinfo is not healthy: %w", err)
	}
	a.Out("  OK")
	if active, err := normalUSBTetherActive(ctx, a.Runner); err != nil {
		return err
	} else if active {
		return fmt.Errorf("normal Android USB tethering is still ON. Turn USB tethering OFF, keep the USB data cable connected, then retry")
	}
	hostIP, err := detectHostIPv4(ctx, a.Runner)
	if err != nil {
		return err
	}
	if err := a.validateSunshineAtHostIP(ctx, hostIP); err != nil {
		return fmt.Errorf("selected Moonlight host address is not Sunshine-safe: %w", err)
	}
	a.Out("  USB-routed Moonlight host: %s", hostIP)
	a.Out("  Sunshine advertises the same LocalIP: OK")

	a.Out("[2/7] Pinned USB dependencies")
	adb, gni, err := a.ensureDependencies(ctx)
	if err != nil {
		return err
	}
	// gnirehtet shells out to adb and also resolves its APK. Pin both to our
	// private dependency directory so the caller's working directory is irrelevant.
	if err := configureGnirehtetEnvironment(adb, gni); err != nil {
		return err
	}
	a.Out("  OK")

	a.Out("[3/7] Android client over ADB")
	serial, err := a.waitADB(ctx, adb, 90*time.Second)
	if err != nil {
		return err
	}
	a.Out("  Authorized device: %s", serial)

	// Important: clear any stale client because gnirehtet ignores new -r parameters if already running.
	a.Out("[4/7] Resetting stale gnirehtet state")
	_, _, _ = a.Runner.Run(ctx, gni, "stop", serial)
	_, _, _ = a.Runner.Run(ctx, adb, "-s", serial, "reverse", "--remove", "localabstract:gnirehtet")

	_, logs, _, _, _ := a.paths()
	if err := os.MkdirAll(logs, 0o755); err != nil {
		return err
	}
	outLog := filepath.Join(logs, "gnirehtet-relay.out.log")
	errLog := filepath.Join(logs, "gnirehtet-relay.err.log")
	relayPort, err := freeLoopbackTCPPort()
	if err != nil {
		return fmt.Errorf("allocate private relay port: %w", err)
	}
	pid, err := a.Runner.Start(gni, []string{"relay", "-p", strconv.Itoa(relayPort)}, outLog, errLog)
	if err != nil {
		return fmt.Errorf("start gnirehtet relay: %w", err)
	}
	time.Sleep(350 * time.Millisecond)
	if err := VerifyProcessOwner(pid, gni); err != nil {
		return fmt.Errorf("gnirehtet relay exited or changed before client start: %w; logs: %s", err, relayLogTail(outLog, errLog))
	}
	cleanupSession := true
	defer func() {
		if cleanupSession {
			// Failure rollback: do not leave a VPN client, adb reverse mapping, or relay behind.
			_, _, _ = a.Runner.Run(context.Background(), gni, "stop", serial)
			_, _, _ = a.Runner.Run(context.Background(), adb, "-s", serial, "reverse", "--remove", "localabstract:gnirehtet")
			_ = KillVerifiedProcess(pid, gni)
		}
	}()

	// `start` installs the client if required. Use the same randomized relay port
	// on both ends so an unrelated/stale gnirehtet process on the default port
	// cannot accidentally satisfy this run.
	_, stderr, err := a.Runner.Run(ctx, gni, "start", serial, "-p", strconv.Itoa(relayPort), "-r", hostIP+"/32")
	if err != nil {
		return fmt.Errorf("start gnirehtet client: %v (%s)", err, strings.TrimSpace(stderr))
	}
	a.Out("  TABLET: approve the gnirehtet VPN/Connection request if it appears.")
	if err := a.waitRelayConnected([]string{outLog, errLog}, 90*time.Second); err != nil {
		return fmt.Errorf("gnirehtet VPN never completed its end-to-end ADB relay connection: %w; relay logs: %s", err, relayLogTail(outLog, errLog))
	}
	a.Out("  Relay connected")

	probe, cleanupProbe, err := a.prepareAndroidProbe(ctx, adb, serial)
	if err != nil {
		return fmt.Errorf("prepare tablet self-test: %w", err)
	}
	defer cleanupProbe()

	a.Out("[5/7] Tablet -> Sunshine TCP over USB")
	if err := a.tcpSunshineProbe(ctx, adb, serial, probe, hostIP); err != nil {
		return fmt.Errorf("USB TCP self-test failed: %w", err)
	}
	a.Out("  OK: tablet reached Sunshine and Sunshine advertised %s", hostIP)

	a.Out("[6/7] Tablet -> PC UDP over USB")
	if err := a.udpProbe(ctx, adb, serial, probe, hostIP); err != nil {
		return fmt.Errorf("USB UDP self-test failed: %w", err)
	}
	a.Out("  OK: UDP survived the ADB/VPN relay")

	st := State{HostIP: hostIP, Serial: serial, RelayPID: pid, RelayPort: relayPort, GnirehtetPath: gni, ADBPath: adb, StartedAt: time.Now()}
	if err := a.saveState(st); err != nil {
		return err
	}
	if err := a.enrollAfterHealthySession(ctx, adb, serial); err != nil {
		_, _, statePath, _, _ := a.paths()
		_ = os.Remove(statePath)
		return fmt.Errorf("persist Android enrollment: %w", err)
	}
	cleanupSession = false

	a.Out("[7/7] READY")
	a.Out("  USB transport passed TCP + UDP self-tests before Moonlight.")
	a.Out("  Android Wi-Fi may stay OFF. Keep normal Android USB tethering OFF.")
	if out, _, err := a.Runner.Run(ctx, adb, "-s", serial, "shell", "pm", "path", MoonlightPackage); err == nil && strings.Contains(out, "package:") {
		if _, _, launchErr := a.Runner.Run(ctx, adb, "-s", serial, "shell", "am", "start", "-n", MoonlightPackage+"/.PcView"); launchErr == nil {
			a.Out("  Moonlight opened on Android.")
		}
	} else {
		a.Out("  Moonlight is not installed. Install the official Moonlight Game Streaming app (package %s).", MoonlightPackage)
		_, _, _ = a.Runner.Run(ctx, adb, "-s", serial, "shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", "market://details?id="+MoonlightPackage)
	}
	a.Out("  In Moonlight tap + and add PC: %s", hostIP)
	a.Out("  Sunshine PIN page: %s", SunshinePINURL)
	return nil
}

func (a *App) Stop(ctx context.Context) error {
	st, err := a.loadState()
	if err != nil {
		if os.IsNotExist(err) {
			a.Out("No active bridge state found.")
			return nil
		}
		return err
	}
	if st.ADBPath != "" {
		_ = os.Setenv("ADB", st.ADBPath)
	}
	if st.GnirehtetPath != "" && st.Serial != "" {
		_, _, _ = a.Runner.Run(ctx, st.GnirehtetPath, "stop", st.Serial)
	}
	if st.ADBPath != "" && st.Serial != "" {
		_, _, _ = a.Runner.Run(ctx, st.ADBPath, "-s", st.Serial, "reverse", "--remove", "localabstract:gnirehtet")
	}
	if st.RelayPID > 0 && st.GnirehtetPath != "" {
		_ = KillVerifiedProcess(st.RelayPID, st.GnirehtetPath)
	}
	_, _, p, _, _ := a.paths()
	_ = os.Remove(p)
	a.Out("FSDL USB bridge stopped. Sunshine/VDD were not touched.")
	return nil
}

func (a *App) Cleanup(ctx context.Context) error {
	_ = a.Stop(ctx)
	tools, _, _, adb, _ := a.paths()
	serial := a.enrolledSerial()
	if _, err := os.Stat(adb); err == nil {
		if serial == "" {
			if out, _, runErr := a.Runner.Run(ctx, adb, "devices"); runErr == nil {
				serial, _ = SingleAuthorizedDevice(ParseADBDevices(out))
			}
		}
		if serial != "" {
			_, _, _ = a.Runner.Run(ctx, adb, "-s", serial, "shell", "rm", "-f", remoteProbePath)
			_, _, _ = a.Runner.Run(ctx, adb, "-s", serial, "uninstall", "com.genymobile.gnirehtet")
		}
		// Our private adb executable may still own the server process and keep itself locked on Windows.
		_, _, _ = a.Runner.Run(ctx, adb, "kill-server")
	}
	runtimeDir := filepath.Join(a.Root, "runtime")
	if err := os.RemoveAll(runtimeDir); err != nil {
		return fmt.Errorf("remove bridge runtime: %w", err)
	}
	_ = tools // kept explicit: tools is intentionally a child of runtimeDir
	a.Out("Cleanup complete. Bridge runtime + test APK removed; Sunshine/VDD/Moonlight were not touched.")
	return nil
}

func (a *App) Prepare(ctx context.Context) error {
	a.Out("FSDL %s dependency preparation", Version)
	adb, gni, err := a.ensureDependencies(ctx)
	if err != nil {
		return err
	}
	if err := configureGnirehtetEnvironment(adb, gni); err != nil {
		return err
	}
	a.Out("Pinned ADB %s and gnirehtet %s are verified and ready.", AdbVersion, GnirehtetVersion)
	return nil
}

func (a *App) Doctor(ctx context.Context) error {
	a.Out("FSDL USB Bridge doctor")
	if err := HTTP200(ctx, a.HTTP, a.SunshineURL); err != nil {
		return fmt.Errorf("Sunshine: FAIL: %w", err)
	}
	a.Out("Sunshine /serverinfo: PASS")
	_, _, _, adb, gni := a.paths()
	for name, p := range map[string]string{"adb": adb, "gnirehtet": gni} {
		if _, err := os.Stat(p); err != nil {
			a.Out("%s: not downloaded yet", name)
		} else {
			a.Out("%s: present", name)
		}
	}

	if e, err := a.loadEnrollment(); err == nil {
		label := strings.TrimSpace(strings.Join([]string{e.Brand, e.Model}, " "))
		if label == "" {
			label = e.Serial
		}
		a.Out("Android enrollment: %s (%s)", label, e.Serial)
	} else if os.IsNotExist(err) {
		a.Out("Android enrollment: none yet")
	} else {
		a.Out("Android enrollment: unreadable (%v)", err)
	}

	if st, err := a.loadState(); err == nil && st.HostIP != "" {
		a.Out("Active Moonlight host: %s", st.HostIP)
		if a.activeStateHealthy(ctx, st.Serial) {
			a.Out("USB bridge state: PASS")
		} else {
			a.Out("USB bridge state: STALE/RECONNECTING")
		}
	} else {
		a.Out("USB bridge state: idle")
	}
	return nil
}

func WaitForEnter(msg string) {
	fmt.Print(msg)
	_, _ = bufio.NewReader(os.Stdin).ReadString('\n')
}

func ParsePID(s string) int {
	n, _ := strconv.Atoi(strings.TrimSpace(s))
	return n
}
