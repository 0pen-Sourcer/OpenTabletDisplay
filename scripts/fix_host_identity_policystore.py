from pathlib import Path

p = Path('installer/common.ps1')
text = p.read_text(encoding='utf-8')
start_marker = 'function New-OTDHostIdentity {'
end_marker = 'function Test-OTDSunshineHostIdentity([string]$IPAddress) {'
if text.count(start_marker) != 1 or text.count(end_marker) != 1:
    raise SystemExit('host-identity function markers were not unique')
start = text.index(start_marker)
end = text.index(end_marker)
replacement = r'''function Get-OTDOwnedHostAddressEntries([string]$IPAddress, [uint32]$InterfaceIndex, [string]$PolicyStore) {
    return @(Get-NetIPAddress -PolicyStore $PolicyStore -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -IPAddress $IPAddress -PrefixLength 32 -SkipAsSource $true -ErrorAction SilentlyContinue)
}

function Remove-OTDOwnedHostAddress([string]$IPAddress, [uint32]$InterfaceIndex) {
    $removed = $false
    foreach ($store in @('ActiveStore','PersistentStore')) {
        $owned = @(Get-OTDOwnedHostAddressEntries $IPAddress $InterfaceIndex $store)
        if ($owned.Count -eq 0) { continue }
        Remove-NetIPAddress -PolicyStore $store -InterfaceIndex $InterfaceIndex -AddressFamily IPv4 -IPAddress $IPAddress -PrefixLength 32 -SkipAsSource $true -Confirm:$false -ErrorAction SilentlyContinue
        $removed = $true
    }
    return $removed
}

function New-OTDHostIdentity {
    $loopIndex = Get-OTDLoopbackInterfaceIndex
    $existing = Get-OTDHostIdentity
    $candidates = New-Object System.Collections.Generic.List[string]
    if ($existing -and $existing.ip) { $candidates.Add([string]$existing.ip) }
    foreach ($candidate in @('172.31.255.254','172.30.255.254','10.255.255.254','192.168.255.254')) {
        if (-not $candidates.Contains($candidate)) { $candidates.Add($candidate) }
    }
    foreach ($candidate in $candidates) {
        $active = @(Get-NetIPAddress -PolicyStore ActiveStore -AddressFamily IPv4 -IPAddress $candidate -ErrorAction SilentlyContinue)
        $persistent = @(Get-NetIPAddress -PolicyStore PersistentStore -AddressFamily IPv4 -IPAddress $candidate -ErrorAction SilentlyContinue)
        $activeOwned = @(Get-OTDOwnedHostAddressEntries $candidate $loopIndex 'ActiveStore')
        $persistentOwned = @(Get-OTDOwnedHostAddressEntries $candidate $loopIndex 'PersistentStore')
        $isRecordedIdentity = ($existing -and [string]$existing.ip -eq $candidate -and [uint32]$existing.interface_index -eq $loopIndex)

        if ($isRecordedIdentity -and $activeOwned.Count -gt 0 -and $persistentOwned.Count -gt 0) {
            return $existing
        }

        # If our ownership record still matches and every existing copy matches the
        # /32 + SkipAsSource fingerprint, repair partial state by recreating both stores.
        if ($isRecordedIdentity -and ($active.Count + $persistent.Count) -gt 0 -and
            ($active.Count + $persistent.Count) -eq ($activeOwned.Count + $persistentOwned.Count)) {
            [void](Remove-OTDOwnedHostAddress $candidate $loopIndex)
            $active = @()
            $persistent = @()
        }

        # Never overwrite an address that exists without our exact ownership fingerprint.
        if (($active.Count + $persistent.Count) -gt 0) { continue }
        if (Test-OTDHostRouteConflict $candidate $loopIndex) { continue }
        try {
            # Omitting PolicyStore intentionally creates both ActiveStore and PersistentStore.
            New-NetIPAddress -InterfaceIndex $loopIndex -IPAddress $candidate -PrefixLength 32 -AddressFamily IPv4 -SkipAsSource $true -ErrorAction Stop | Out-Null
            Start-Sleep -Milliseconds 250
            $activeOwned = @(Get-OTDOwnedHostAddressEntries $candidate $loopIndex 'ActiveStore')
            $persistentOwned = @(Get-OTDOwnedHostAddressEntries $candidate $loopIndex 'PersistentStore')
            if ($activeOwned.Count -eq 0 -or $persistentOwned.Count -eq 0) {
                throw 'Windows did not retain the owned /32 in both ActiveStore and PersistentStore.'
            }
            $identity = [ordered]@{
                schema = 1
                ip = $candidate
                mode = 'loopback_alias'
                interface_index = $loopIndex
                prefix_length = 32
                skip_as_source = $true
                managed_by_opentabletdisplay = $true
                created_at = (Get-Date -Format o)
            }
            Save-FSDLJson $identity $script:HostIdentityPath
            return [pscustomobject]$identity
        } catch {
            [void](Remove-OTDOwnedHostAddress $candidate $loopIndex)
        }
    }
    throw 'No conflict-free RFC1918 /32 address was available for the OpenTabletDisplay host-only identity.'
}

function Remove-OTDHostIdentity($Identity) {
    if (-not $Identity -or -not [bool]$Identity.managed_by_opentabletdisplay) { return $false }
    $ip = [string]$Identity.ip
    $index = [uint32]$Identity.interface_index
    return (Remove-OTDOwnedHostAddress $ip $index)
}

'''
p.write_text(text[:start] + replacement + text[end:], encoding='utf-8')
