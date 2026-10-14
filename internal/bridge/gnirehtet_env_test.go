package bridge

import (
	"os"
	"path/filepath"
	"testing"
)

func TestConfigureGnirehtetEnvironmentUsesAbsoluteAPK(t *testing.T) {
	oldADB, hadADB := os.LookupEnv("ADB")
	oldAPK, hadAPK := os.LookupEnv("GNIREHTET_APK")
	t.Cleanup(func() {
		if hadADB {
			_ = os.Setenv("ADB", oldADB)
		} else {
			_ = os.Unsetenv("ADB")
		}
		if hadAPK {
			_ = os.Setenv("GNIREHTET_APK", oldAPK)
		} else {
			_ = os.Unsetenv("GNIREHTET_APK")
		}
	})

	dir := t.TempDir()
	gni := filepath.Join(dir, "gnirehtet.exe")
	apk := filepath.Join(dir, "gnirehtet.apk")
	adb := filepath.Join(dir, "platform-tools", "adb.exe")
	if err := os.WriteFile(apk, []byte("test-apk"), 0600); err != nil {
		t.Fatal(err)
	}

	if err := configureGnirehtetEnvironment(adb, gni); err != nil {
		t.Fatal(err)
	}
	if got := os.Getenv("ADB"); got != adb {
		t.Fatalf("ADB=%q, want %q", got, adb)
	}
	if got := os.Getenv("GNIREHTET_APK"); got != apk {
		t.Fatalf("GNIREHTET_APK=%q, want %q", got, apk)
	}
	if !filepath.IsAbs(os.Getenv("GNIREHTET_APK")) {
		t.Fatalf("APK path must be absolute")
	}
}

func TestConfigureGnirehtetEnvironmentRejectsMissingAPK(t *testing.T) {
	dir := t.TempDir()
	err := configureGnirehtetEnvironment(filepath.Join(dir, "adb.exe"), filepath.Join(dir, "gnirehtet.exe"))
	if err == nil {
		t.Fatal("expected missing APK error")
	}
}
