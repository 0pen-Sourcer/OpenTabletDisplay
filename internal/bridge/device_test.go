package bridge

import (
	"strings"
	"testing"
)

func TestSelectAuthorizedDeviceUsesEnrollment(t *testing.T) {
	devs := map[string]string{"A": "device", "B": "device"}
	got, err := SelectAuthorizedDevice(devs, "B")
	if err != nil || got != "B" {
		t.Fatalf("got %q err=%v", got, err)
	}
}

func TestSelectAuthorizedDeviceRejectsMultipleWithoutEnrollment(t *testing.T) {
	_, err := SelectAuthorizedDevice(map[string]string{"A": "device", "B": "device"}, "")
	if err == nil || !strings.Contains(err.Error(), "multiple authorized") {
		t.Fatalf("expected multiple-device error, got %v", err)
	}
}

func TestSelectAuthorizedDeviceReportsUnauthorized(t *testing.T) {
	_, err := SelectAuthorizedDevice(map[string]string{"A": "unauthorized"}, "")
	if err == nil || !strings.Contains(err.Error(), "authorization") {
		t.Fatalf("expected authorization error, got %v", err)
	}
}

func TestProbeFilenameForABI(t *testing.T) {
	cases := map[string]string{
		"arm64-v8a":   "fsdl-probe-android-arm64",
		"x86_64":      "",
		"x86":         "",
		"armeabi-v7a": "",
		"weird":       "",
	}
	for abi, want := range cases {
		if got := probeFilenameForABI(abi); got != want {
			t.Fatalf("ABI %s: got %q want %q", abi, got, want)
		}
	}
}
