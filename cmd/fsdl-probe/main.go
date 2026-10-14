package main

import (
	"bufio"
	"fmt"
	"io"
	"net"
	"os"
	"strconv"
	"strings"
	"time"
)

func die(msg string, args ...any) {
	fmt.Fprintf(os.Stderr, msg+"\n", args...)
	os.Exit(2)
}

func main() {
	if len(os.Args) < 2 {
		die("usage: fsdl-probe tcp HOST PORT | connect HOST PORT | udp HOST PORT TOKEN")
	}
	switch os.Args[1] {
	case "tcp":
		if len(os.Args) != 4 {
			die("usage: fsdl-probe tcp HOST PORT")
		}
		port, err := strconv.Atoi(os.Args[3])
		if err != nil {
			die("bad port")
		}
		addr := net.JoinHostPort(os.Args[2], strconv.Itoa(port))
		c, err := net.DialTimeout("tcp4", addr, 5*time.Second)
		if err != nil {
			die("dial: %v", err)
		}
		defer c.Close()
		_ = c.SetDeadline(time.Now().Add(6 * time.Second))
		_, err = fmt.Fprintf(c, "GET /serverinfo HTTP/1.0\r\nHost: %s\r\nConnection: close\r\n\r\n", os.Args[2])
		if err != nil {
			die("write: %v", err)
		}
		r := bufio.NewReader(c)
		line, err := r.ReadString('\n')
		if err != nil {
			die("read status: %v", err)
		}
		line = strings.TrimSpace(line)
		fmt.Println(line)
		if !strings.Contains(line, " 200 ") && !strings.HasSuffix(line, " 200") {
			die("unexpected HTTP status: %s", line)
		}
		rest, err := io.ReadAll(io.LimitReader(r, 256*1024))
		if err != nil {
			die("read body: %v", err)
		}
		fmt.Print(string(rest))
	case "connect":
		if len(os.Args) != 4 {
			die("usage: fsdl-probe connect HOST PORT")
		}
		port, err := strconv.Atoi(os.Args[3])
		if err != nil {
			die("bad port")
		}
		addr := net.JoinHostPort(os.Args[2], strconv.Itoa(port))
		c, err := net.DialTimeout("tcp4", addr, 5*time.Second)
		if err != nil {
			die("dial: %v", err)
		}
		_ = c.Close()
		fmt.Println("connected")
	case "udp":
		if len(os.Args) != 5 {
			die("usage: fsdl-probe udp HOST PORT TOKEN")
		}
		port, err := strconv.Atoi(os.Args[3])
		if err != nil {
			die("bad port")
		}
		c, err := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.ParseIP(os.Args[2]), Port: port})
		if err != nil {
			die("udp dial: %v", err)
		}
		defer c.Close()
		_ = c.SetWriteDeadline(time.Now().Add(3 * time.Second))
		if _, err := c.Write([]byte(os.Args[4])); err != nil {
			die("udp write: %v", err)
		}
		fmt.Println("sent")
	default:
		die("unknown mode")
	}
}
