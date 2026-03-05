# ==========================================================
# bye-tcp-internet — Post-Apply Verification Script
# Author: ceo714
# GitHub: https://github.com/ceo714/bye-tcp-internet
# Version: 1.2.0
# ==========================================================
#
# Usage:
#   Right-click → "Run with PowerShell"
#   Or: powershell -ExecutionPolicy Bypass -File verify.ps1
# ==========================================================

$ErrorActionPreference = "SilentlyContinue"

# --- Color helpers ---
function Pass($msg)  { Write-Host "  [PASS] $msg" -ForegroundColor Green }
function Fail($msg)  { Write-Host "  [FAIL] $msg" -ForegroundColor Red }
function Info($msg)  { Write-Host "  [INFO] $msg" -ForegroundColor Cyan }
function Head($msg)  { Write-Host "`n$msg" -ForegroundColor White }

# --- Registry read helper ---
function Get-RegValue($path, $name) {
    try {
        return (Get-ItemProperty -Path $path -Name $name -ErrorAction Stop).$name
    } catch {
        return $null
    }
}

# --- Detect active profile ---
$profileName = "UNKNOWN"
$mmcssGames  = Get-RegValue "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games" "Scheduling Category"
$negDns      = Get-RegValue "HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters" "NegativeCacheTime"

if ($mmcssGames -eq "High" -and $negDns -eq 0) {
    $profileName = "GMVELOCITY (Low-Latency)"
} elseif ($null -ne (Get-RegValue "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" "TcpTimedWaitDelay")) {
    $profileName = "UNIVERSAL (Baseline)"
} else {
    $profileName = "NONE / ROLLBACK applied"
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor DarkGray
Write-Host "  bye-tcp-internet — Verification v1.2.0  " -ForegroundColor White
Write-Host "==========================================" -ForegroundColor DarkGray
Write-Host "  Detected profile: $profileName" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor DarkGray


# ==========================================================
# BLOCK 1: TCP/IP Core Parameters
# ==========================================================
Head "[ TCP/IP Core Parameters ]"

$tcpPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"

$ttl = Get-RegValue $tcpPath "DefaultTTL"
if ($ttl -eq 64) { Pass "DefaultTTL = 64" } else { Fail "DefaultTTL = $ttl (expected 64)" }

$pmtu = Get-RegValue $tcpPath "EnablePMTUDiscovery"
if ($pmtu -eq 1) { Pass "EnablePMTUDiscovery = 1 (ON)" } else { Fail "EnablePMTUDiscovery = $pmtu (expected 1)" }

$pmtubh = Get-RegValue $tcpPath "EnablePMTUBHDetect"
if ($pmtubh -eq 0) { Pass "EnablePMTUBHDetect = 0 (OFF)" } else { Fail "EnablePMTUBHDetect = $pmtubh (expected 0)" }

$sack = Get-RegValue $tcpPath "SackOpts"
if ($sack -eq 1) { Pass "SackOpts = 1 (ON)" } else { Fail "SackOpts = $sack (expected 1)" }

$rfc = Get-RegValue $tcpPath "Tcp1323Opts"
if ($rfc -eq 1) { Pass "Tcp1323Opts = 1 (Window Scaling ON)" } else { Fail "Tcp1323Opts = $rfc (expected 1)" }

$port = Get-RegValue $tcpPath "MaxUserPort"
if ($port -eq 65534) { Pass "MaxUserPort = 65534 (expanded)" } else { Fail "MaxUserPort = $port (expected 65534)" }

$tw = Get-RegValue $tcpPath "TcpTimedWaitDelay"
if ($tw -eq 30) { Pass "TcpTimedWaitDelay = 30s (reduced)" } else { Fail "TcpTimedWaitDelay = $tw (expected 30)" }

$ecn = Get-RegValue $tcpPath "EnableECNCapability"
if ($ecn -eq 0) { Pass "EnableECNCapability = 0 (OFF)" } else { Fail "EnableECNCapability = $ecn (expected 0)" }


# ==========================================================
# BLOCK 2: DNS Cache Parameters
# ==========================================================
Head "[ DNS Cache Parameters ]"

$dnsPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters"

$maxNeg = Get-RegValue $dnsPath "MaxNegativeCacheTtl"
$negCache = Get-RegValue $dnsPath "NegativeCacheTime"
$netFail  = Get-RegValue $dnsPath "NetFailureCacheTime"

if ($profileName -match "GMVELOCITY") {
    if ($maxNeg  -eq 0) { Pass "MaxNegativeCacheTtl = 0" }  else { Fail "MaxNegativeCacheTtl = $maxNeg (expected 0)" }
    if ($negCache -eq 0) { Pass "NegativeCacheTime = 0" }    else { Fail "NegativeCacheTime = $negCache (expected 0)" }
    if ($netFail  -eq 0) { Pass "NetFailureCacheTime = 0" }  else { Fail "NetFailureCacheTime = $netFail (expected 0)" }
} else {
    if ($maxNeg  -eq 60) { Pass "MaxNegativeCacheTtl = 60s" }  else { Fail "MaxNegativeCacheTtl = $maxNeg (expected 60)" }
    if ($negCache -eq 60) { Pass "NegativeCacheTime = 60s" }    else { Fail "NegativeCacheTime = $negCache (expected 60)" }
    if ($netFail  -eq 30) { Pass "NetFailureCacheTime = 30s" }  else { Fail "NetFailureCacheTime = $netFail (expected 30)" }
}


# ==========================================================
# BLOCK 3: Multimedia / MMCSS
# ==========================================================
Head "[ Multimedia & MMCSS ]"

$mmPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"

$nti = Get-RegValue $mmPath "NetworkThrottlingIndex"
if ($nti -eq 4294967295) { Pass "NetworkThrottlingIndex = 0xFFFFFFFF (unlimited)" } else { Fail "NetworkThrottlingIndex = $nti (expected 0xFFFFFFFF)" }

$sr = Get-RegValue $mmPath "SystemResponsiveness"
if ($profileName -match "GMVELOCITY") {
    if ($sr -eq 0) { Pass "SystemResponsiveness = 0 (gaming)" } else { Fail "SystemResponsiveness = $sr (expected 0 for gaming)" }
} else {
    if ($sr -eq 20) { Pass "SystemResponsiveness = 20 (balanced)" } else { Fail "SystemResponsiveness = $sr (expected 20)" }
}

if ($profileName -match "GMVELOCITY") {
    $gamesPath = "$mmPath\Tasks\Games"
    $gpuPrio   = Get-RegValue $gamesPath "GPU Priority"
    $taskPrio  = Get-RegValue $gamesPath "Priority"
    $schedCat  = Get-RegValue $gamesPath "Scheduling Category"
    $sfioPrio  = Get-RegValue $gamesPath "SFIO Priority"

    if ($gpuPrio  -eq 8)      { Pass "Tasks\Games GPU Priority = 8" }         else { Fail "Tasks\Games GPU Priority = $gpuPrio (expected 8)" }
    if ($taskPrio -eq 6)      { Pass "Tasks\Games Priority = 6" }             else { Fail "Tasks\Games Priority = $taskPrio (expected 6)" }
    if ($schedCat -eq "High") { Pass "Tasks\Games Scheduling Category = High" } else { Fail "Tasks\Games Scheduling Category = $schedCat (expected High)" }
    if ($sfioPrio -eq "High") { Pass "Tasks\Games SFIO Priority = High" }     else { Fail "Tasks\Games SFIO Priority = $sfioPrio (expected High)" }
} else {
    Info "MMCSS Tasks\Games check skipped (not a GMVELOCITY profile)"
}


# ==========================================================
# BLOCK 4: QoS Policy
# ==========================================================
Head "[ QoS Policy ]"

$qosVal = Get-RegValue "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Psched" "NonBestEffortLimit"
if ($qosVal -eq 0) { Pass "NonBestEffortLimit = 0 (no reserved bandwidth)" } else { Fail "NonBestEffortLimit = $qosVal (expected 0)" }


# ==========================================================
# BLOCK 5: netsh live read (informational)
# ==========================================================
Head "[ Live TCP Stack — netsh int tcp show global ]"
Write-Host ""
netsh int tcp show global 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }


# ==========================================================
# BLOCK 6: PowerShell Get-NetTCPSetting (informational)
# ==========================================================
Head "[ Live TCP Setting — Get-NetTCPSetting Internet ]"
Write-Host ""
try {
    Get-NetTCPSetting -SettingName Internet | Format-List | Out-String -Stream |
        ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
} catch {
    Info "Get-NetTCPSetting not available on this OS version."
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor DarkGray
Write-Host "  Verification complete." -ForegroundColor White
Write-Host "  If any [FAIL] — re-apply the .reg and reboot." -ForegroundColor DarkGray
Write-Host "==========================================" -ForegroundColor DarkGray
Write-Host ""
