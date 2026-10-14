package bridge

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"
)

// Enrollment pins FSDL to one Android client after the first successful
// end-to-end USB transport validation. This prevents a second debugging-enabled
// phone from silently hijacking the display session.
type Enrollment struct {
	Serial     string    `json:"serial"`
	Brand      string    `json:"brand,omitempty"`
	Model      string    `json:"model,omitempty"`
	Product    string    `json:"product,omitempty"`
	EnrolledAt time.Time `json:"enrolled_at"`
}

func (a *App) enrollmentPath() string {
	return filepath.Join(a.Root, "enrollment.json")
}

func (a *App) loadEnrollment() (Enrollment, error) {
	b, err := os.ReadFile(a.enrollmentPath())
	if err != nil {
		return Enrollment{}, err
	}
	var e Enrollment
	if err := json.Unmarshal(b, &e); err != nil {
		return Enrollment{}, err
	}
	if strings.TrimSpace(e.Serial) == "" {
		return Enrollment{}, errors.New("enrollment file has no serial")
	}
	return e, nil
}

func (a *App) saveEnrollment(e Enrollment) error {
	if strings.TrimSpace(e.Serial) == "" {
		return errors.New("cannot save empty enrollment serial")
	}
	p := a.enrollmentPath()
	if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
		return err
	}
	b, err := json.MarshalIndent(e, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(p, b, 0o600)
}

func (a *App) ResetEnrollment(ctx context.Context) error {
	_ = a.Stop(ctx)
	if err := os.Remove(a.enrollmentPath()); err != nil && !os.IsNotExist(err) {
		return err
	}
	a.Out("Android enrollment cleared. The next single authorized device that passes the live USB self-tests will be enrolled.")
	return nil
}

func authorizedSerials(devs map[string]string) []string {
	out := make([]string, 0, len(devs))
	for serial, state := range devs {
		if state == "device" {
			out = append(out, serial)
		}
	}
	sort.Strings(out)
	return out
}

// SelectAuthorizedDevice implements the safe selection contract:
//   - if a device is enrolled, only that serial may be selected;
//   - without enrollment, exactly one authorized device is required;
//   - multiple devices are never chosen by ordering/guessing.
func SelectAuthorizedDevice(devs map[string]string, enrolledSerial string) (string, error) {
	enrolledSerial = strings.TrimSpace(enrolledSerial)
	if enrolledSerial != "" {
		state, ok := devs[enrolledSerial]
		if !ok {
			return "", fmt.Errorf("enrolled Android device %s is not connected", enrolledSerial)
		}
		if state != "device" {
			return "", fmt.Errorf("enrolled Android device %s is %s", enrolledSerial, state)
		}
		return enrolledSerial, nil
	}

	authorized := authorizedSerials(devs)
	switch len(authorized) {
	case 0:
		for serial, state := range devs {
			if state == "unauthorized" {
				return "", fmt.Errorf("Android device %s is waiting for USB-debugging authorization", serial)
			}
			if state == "offline" {
				return "", fmt.Errorf("Android device %s is offline in ADB", serial)
			}
		}
		return "", errors.New("no authorized Android device")
	case 1:
		return authorized[0], nil
	default:
		return "", fmt.Errorf("multiple authorized Android devices are connected: %s; disconnect extras or enroll one explicitly", strings.Join(authorized, ", "))
	}
}

func (a *App) enrolledSerial() string {
	e, err := a.loadEnrollment()
	if err != nil {
		return ""
	}
	return e.Serial
}

func (a *App) waitADB(ctx context.Context, adb string, timeout time.Duration) (string, error) {
	_, _, _ = a.Runner.Run(ctx, adb, "start-server")
	deadline := time.Now().Add(timeout)
	lastMessage := ""
	for time.Now().Before(deadline) {
		out, _, _ := a.Runner.Run(ctx, adb, "devices")
		devs := ParseADBDevices(out)
		enrolled := a.enrolledSerial()
		if serial, err := SelectAuthorizedDevice(devs, enrolled); err == nil {
			return serial, nil
		} else {
			msg := err.Error()
			if msg != lastMessage {
				switch {
				case strings.Contains(msg, "authorization"):
					a.Out("ANDROID ACTION: unlock the device and tap Allow on the USB debugging RSA prompt.")
				case strings.Contains(msg, "multiple authorized"):
					a.Out("FSDL will not guess between multiple Android devices. Disconnect extras or run `FSDL.exe enroll SERIAL`.")
				case strings.Contains(msg, "not connected") && enrolled != "":
					a.Out("Waiting for enrolled Android device %s...", enrolled)
				case strings.Contains(msg, "no authorized"):
					a.Out("Waiting for Android USB debugging. Use a USB data cable and enable Developer options -> USB debugging.")
				}
				lastMessage = msg
			}
		}
		time.Sleep(1500 * time.Millisecond)
	}
	return "", fmt.Errorf("no selectable authorized Android device appeared within %s", timeout)
}

func (a *App) deviceProp(ctx context.Context, adb, serial, prop string) string {
	out, _, err := a.Runner.Run(ctx, adb, "-s", serial, "shell", "getprop", prop)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(out)
}

func (a *App) enrollAfterHealthySession(ctx context.Context, adb, serial string) error {
	if current, err := a.loadEnrollment(); err == nil {
		if current.Serial != serial {
			return fmt.Errorf("healthy session serial %s does not match enrolled serial %s", serial, current.Serial)
		}
		return nil
	}
	e := Enrollment{
		Serial:     serial,
		Brand:      a.deviceProp(ctx, adb, serial, "ro.product.brand"),
		Model:      a.deviceProp(ctx, adb, serial, "ro.product.model"),
		Product:    a.deviceProp(ctx, adb, serial, "ro.product.name"),
		EnrolledAt: time.Now(),
	}
	if err := a.saveEnrollment(e); err != nil {
		return err
	}
	label := strings.TrimSpace(strings.Join([]string{e.Brand, e.Model}, " "))
	if label == "" {
		label = serial
	}
	a.Out("  Enrolled Android display: %s (%s)", label, serial)
	return nil
}

func (a *App) Enroll(ctx context.Context, serial string) error {
	serial = strings.TrimSpace(serial)
	if serial == "" {
		return errors.New("usage: FSDL.exe enroll SERIAL")
	}
	adb, _, err := a.ensureDependencies(ctx)
	if err != nil {
		return err
	}
	out, _, err := a.Runner.Run(ctx, adb, "devices")
	if err != nil {
		return err
	}
	devs := ParseADBDevices(out)
	if devs[serial] != "device" {
		return fmt.Errorf("device %s is not currently authorized in ADB", serial)
	}
	_ = a.Stop(ctx)
	if err := os.Remove(a.enrollmentPath()); err != nil && !os.IsNotExist(err) {
		return err
	}
	return a.enrollAfterHealthySession(ctx, adb, serial)
}

func (a *App) Devices(ctx context.Context) error {
	adb, _, err := a.ensureDependencies(ctx)
	if err != nil {
		return err
	}
	out, _, err := a.Runner.Run(ctx, adb, "devices")
	if err != nil {
		return err
	}
	devs := ParseADBDevices(out)
	enrolled := a.enrolledSerial()
	if len(devs) == 0 {
		a.Out("No Android devices visible to ADB.")
		return nil
	}
	serials := make([]string, 0, len(devs))
	for serial := range devs {
		serials = append(serials, serial)
	}
	sort.Strings(serials)
	for _, serial := range serials {
		mark := ""
		if serial == enrolled {
			mark = " [enrolled]"
		}
		a.Out("%s\t%s%s", serial, devs[serial], mark)
	}
	return nil
}
