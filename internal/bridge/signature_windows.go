//go:build windows

package bridge

import (
	"context"
	"fmt"
	"strings"
)

func verifyPlatformToolsSignature(ctx context.Context, r Runner, adbPath string) error {
	q := strings.ReplaceAll(adbPath, "'", "''")
	script := fmt.Sprintf(`$s=Get-AuthenticodeSignature -LiteralPath '%s'; if($s.Status -ne 'Valid'){Write-Error ('adb.exe Authenticode status: '+$s.Status); exit 31}; if($null -eq $s.SignerCertificate -or $s.SignerCertificate.Subject -notmatch 'Google LLC'){Write-Error ('Unexpected adb.exe signer: '+$s.SignerCertificate.Subject); exit 32}; Write-Output $s.SignerCertificate.Subject`, q)
	out, er, err := r.Run(ctx, "powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script)
	if err != nil {
		return fmt.Errorf("Google Authenticode verification failed: %v (%s%s)", err, strings.TrimSpace(out), strings.TrimSpace(er))
	}
	return nil
}
