<#
.SYNOPSIS
    Lets Edge and Chrome on this PC use the microphone on the call center's
    web app, for Listen & speak (*223, S-62).

.DESCRIPTION
    The web app is served at http://192.168.1.100, not https. Edge and Chrome
    only offer the microphone to a secure address, so at that address they
    never even ask. This sets the browsers' own company policy,
    OverrideSecurityRestrictionsOnInsecureOrigin, to treat that one address
    as secure. Nothing else is trusted, and the traffic is not encrypted by
    it: it is as private as it already was on the office network.

    Run it once on each supervisor PC, as administrator, then restart the
    browser. The browser then asks for the microphone the first time Listen
    & speak is pressed. Running it again changes nothing. -Remove undoes it.

    It is ASCII on purpose: Windows PowerShell 5.1 reads a script with no
    byte-order mark in the PC's own code page.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File allow-microphone.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File allow-microphone.ps1 -Address http://192.168.1.100 -Remove
#>
param(
    # The address the supervisors type, exactly as the browser shows it, with no path.
    [string] $Address = 'http://192.168.1.100',

    # Take the address off again.
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host 'Run this as administrator: right-click PowerShell, Run as administrator.' -ForegroundColor Red
    exit 1
}

$Address = $Address.TrimEnd('/')
if ($Address -notmatch '^https?://[^/\s]+$') {
    Write-Host "'$Address' is not an address like http://192.168.1.100" -ForegroundColor Red
    exit 1
}

# The policy is a list: one numbered value per address, under each browser's key.
$browsers = [ordered]@{
    'Edge'   = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\OverrideSecurityRestrictionsOnInsecureOrigin'
    'Chrome' = 'HKLM:\SOFTWARE\Policies\Google\Chrome\OverrideSecurityRestrictionsOnInsecureOrigin'
}

foreach ($browser in $browsers.Keys) {
    $key = $browsers[$browser]
    if (-not (Test-Path $key)) {
        if ($Remove) {
            Write-Host "${browser}: nothing to remove."
            continue
        }
        New-Item -Path $key -Force | Out-Null
    }

    $values = (Get-Item $key).Property |
        ForEach-Object { [pscustomobject]@{ Name = $_; Value = (Get-ItemProperty -Path $key -Name $_).$_ } }
    $mine = @($values | Where-Object { ([string]$_.Value).TrimEnd('/') -eq $Address })

    if ($Remove) {
        foreach ($v in $mine) {
            Remove-ItemProperty -Path $key -Name $v.Name
        }
        Write-Host "${browser}: $Address removed."
        continue
    }

    if ($mine.Count -gt 0) {
        Write-Host "${browser}: $Address is already allowed."
        continue
    }

    # The next free number, keeping any address someone else put there.
    $next = 1
    while ($values.Name -contains [string]$next) {
        $next++
    }

    New-ItemProperty -Path $key -Name ([string]$next) -Value $Address -PropertyType String | Out-Null
    Write-Host "${browser}: $Address allowed."
}

Write-Host ''
Write-Host 'Close every Edge and Chrome window and open the browser again.'
Write-Host 'To check: edge://policy (or chrome://policy) lists OverrideSecurityRestrictionsOnInsecureOrigin.'
