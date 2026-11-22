package main

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
	"time"

	"github.com/0pen-Sourcer/fsdl/internal/bridge"
)

func rootDir() string {
	exe, err := os.Executable()
	if err != nil {
		return "."
	}
	return filepath.Dir(exe)
}

func usage() {
	fmt.Printf(`FSDL %s

Usage:
  FSDL.exe start              Start/repair the USB transport now
  FSDL.exe daemon             Keep the transport alive in the background
  FSDL.exe stop               Stop FSDL's current relay only
  FSDL.exe prepare            Download and verify private ADB/gnirehtet tools
  FSDL.exe doctor             Print transport/Sunshine health
  FSDL.exe devices            List ADB-visible Android devices
  FSDL.exe enroll SERIAL      Explicitly choose an Android device
  FSDL.exe reset-device       Forget the enrolled Android device
  FSDL.exe cleanup            Remove FSDL's private runtime/tools
  FSDL.exe --selftest         Run offline regression tests
`, bridge.Version)
}

func main() {
	arg := "help" // Never mutate USB state when FSDL.exe is double-clicked without an explicit command.
	if len(os.Args) > 1 {
		arg = strings.ToLower(os.Args[1])
	}
	if arg == "--selftest" || arg == "selftest" {
		if err := bridge.RunSelfTest(); err != nil {
			fmt.Fprintln(os.Stderr, "SELFTEST FAILED:", err)
			os.Exit(1)
		}
		fmt.Println("SELFTEST PASS")
		return
	}

	app := bridge.New(rootDir())
	if arg == "daemon" {
		// The agent can run without a console window. Keep diagnostics in a
		// user-local append-only log instead of hosting a PowerShell loop.
		logDir := filepath.Join(rootDir(), "runtime")
		if err := os.MkdirAll(logDir, 0o700); err != nil {
			fmt.Fprintln(os.Stderr, "Agent log directory:", err)
			os.Exit(1)
		}
		logPath := filepath.Join(logDir, "agent.log")
		if info, err := os.Stat(logPath); err == nil && info.Size() > 2<<20 {
			_ = os.Remove(logPath + ".1")
			_ = os.Rename(logPath, logPath+".1")
		}
		logFile, err := os.OpenFile(logPath, os.O_CREATE|os.O_APPEND|os.O_WRONLY, 0o600)
		if err != nil {
			fmt.Fprintln(os.Stderr, "Agent log:", err)
			os.Exit(1)
		}
		defer logFile.Close()
		app.Out = func(format string, args ...any) {
			_, _ = fmt.Fprintf(logFile, "[%s] %s\n", time.Now().Format(time.RFC3339), fmt.Sprintf(format, args...))
		}
		app.Out("Starting OpenTabletDisplay USB agent %s", bridge.Version)
		ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
		defer cancel()
		if err := app.Daemon(ctx); err != nil {
			app.Out("Agent stopped with error: %v", err)
			os.Exit(1)
		}
		return
	}

	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Minute)
	defer cancel()
	var err error
	switch arg {
	case "start":
		err = app.Start(ctx)
	case "stop":
		err = app.Stop(ctx)
	case "prepare":
		err = app.Prepare(ctx)
	case "cleanup":
		err = app.Cleanup(ctx)
	case "doctor", "status":
		err = app.Doctor(ctx)
	case "devices":
		err = app.Devices(ctx)
	case "enroll":
		serial := ""
		if len(os.Args) > 2 {
			serial = os.Args[2]
		}
		err = app.Enroll(ctx, serial)
	case "reset-device":
		err = app.ResetEnrollment(ctx)
	case "help", "-h", "--help":
		usage()
		return
	default:
		usage()
		os.Exit(2)
	}
	if err != nil {
		fmt.Fprintln(os.Stderr, "FAILED:", err)
		os.Exit(1)
	}
}
