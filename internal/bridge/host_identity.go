package bridge

import (
	"encoding/json"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"strings"
)

const hostIdentityFileName = "host-identity.json"

type hostIdentityConfig struct {
	Schema                     int    `json:"schema"`
	IP                         string `json:"ip"`
	Mode                       string `json:"mode"`
	InterfaceIndex             uint32 `json:"interface_index"`
	PrefixLength               int    `json:"prefix_length"`
	ManagedByOpenTabletDisplay bool   `json:"managed_by_opentabletdisplay"`
}

func loadConfiguredHostIdentity(root string) (hostIdentityConfig, bool, error) {
	path := filepath.Join(root, hostIdentityFileName)
	b, err := os.ReadFile(path)
	if err != nil {
		if os.IsNotExist(err) {
			return hostIdentityConfig{}, false, nil
		}
		return hostIdentityConfig{}, false, fmt.Errorf("read managed host identity: %w", err)
	}
	var cfg hostIdentityConfig
	if err := json.Unmarshal(b, &cfg); err != nil {
		return hostIdentityConfig{}, false, fmt.Errorf("parse managed host identity: %w", err)
	}
	cfg.IP = strings.TrimSpace(cfg.IP)
	parsed := net.ParseIP(cfg.IP)
	if cfg.Schema != 1 || cfg.Mode != "loopback_alias" || !cfg.ManagedByOpenTabletDisplay {
		return hostIdentityConfig{}, false, fmt.Errorf("managed host identity file has unsupported ownership/schema")
	}
	if parsed == nil || parsed.To4() == nil || !parsed.IsPrivate() || parsed.IsLoopback() || parsed.IsLinkLocalUnicast() || cfg.PrefixLength != 32 {
		return hostIdentityConfig{}, false, fmt.Errorf("managed host identity contains unsafe IPv4 %q", cfg.IP)
	}
	return cfg, true, nil
}

func configuredHostIPv4(root string) (string, bool, error) {
	cfg, ok, err := loadConfiguredHostIdentity(root)
	if err != nil || !ok {
		return "", ok, err
	}
	return cfg.IP, true, nil
}

func isConfiguredHostIPv4(root, ip string) bool {
	configured, ok, err := configuredHostIPv4(root)
	return err == nil && ok && configured == strings.TrimSpace(ip)
}
