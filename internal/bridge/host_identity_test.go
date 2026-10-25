package bridge

import (
	"os"
	"path/filepath"
	"testing"
)

func writeIdentity(t *testing.T, root, body string) {
	t.Helper()
	if err := os.WriteFile(filepath.Join(root, hostIdentityFileName), []byte(body), 0o600); err != nil {
		t.Fatal(err)
	}
}

func TestConfiguredHostIdentityPrivateAlias(t *testing.T) {
	root := t.TempDir()
	writeIdentity(t, root, `{"schema":1,"ip":"172.31.255.254","mode":"loopback_alias","interface_index":1,"prefix_length":32,"managed_by_opentabletdisplay":true}`)
	ip, ok, err := configuredHostIPv4(root)
	if err != nil || !ok || ip != "172.31.255.254" {
		t.Fatalf("got ip=%q ok=%v err=%v", ip, ok, err)
	}
}

func TestConfiguredHostIdentityRejectsUnsafeAddresses(t *testing.T) {
	for _, ip := range []string{"127.0.0.2", "8.8.8.8", "169.254.5.5"} {
		t.Run(ip, func(t *testing.T) {
			root := t.TempDir()
			writeIdentity(t, root, `{"schema":1,"ip":"`+ip+`","mode":"loopback_alias","interface_index":1,"prefix_length":32,"managed_by_opentabletdisplay":true}`)
			if _, ok, err := configuredHostIPv4(root); err == nil || ok {
				t.Fatalf("unsafe identity %s was accepted", ip)
			}
		})
	}
}

func TestConfiguredHostIdentityMissingIsFallbackEligible(t *testing.T) {
	if ip, ok, err := configuredHostIPv4(t.TempDir()); err != nil || ok || ip != "" {
		t.Fatalf("got ip=%q ok=%v err=%v", ip, ok, err)
	}
}
