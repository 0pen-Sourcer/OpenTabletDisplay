package bridge

import (
	"strings"
	"testing"
)

func TestValidateServerInfoAddressAcceptsSelectedHost(t *testing.T) {
	wire := "HTTP/1.0 200 OK\r\n\r\n<?xml version=\"1.0\"?><root status_code=\"200\"><LocalIP>192.168.1.23</LocalIP><HttpsPort>47984</HttpsPort><state>SUNSHINE_SERVER_FREE</state></root>"
	if err := validateServerInfoAddress(wire, "192.168.1.23"); err != nil {
		t.Fatal(err)
	}
}

func TestValidateServerInfoAddressRejectsLoopbackAdvertisement(t *testing.T) {
	wire := "HTTP/1.0 200 OK\r\n\r\n<root status_code=\"200\"><LocalIP>127.0.0.1</LocalIP><HttpsPort>47984</HttpsPort><state>SUNSHINE_SERVER_FREE</state></root>"
	err := validateServerInfoAddress(wire, "192.168.1.23")
	if err == nil || !strings.Contains(err.Error(), "would break Moonlight") {
		t.Fatalf("expected address-semantics rejection, got %v", err)
	}
}

func TestValidateServerInfoAddressRequiresHttpsPort(t *testing.T) {
	wire := "HTTP/1.0 200 OK\r\n\r\n<root status_code=\"200\"><LocalIP>192.168.1.23</LocalIP></root>"
	if err := validateServerInfoAddress(wire, "192.168.1.23"); err == nil {
		t.Fatal("expected missing HttpsPort rejection")
	}
}
