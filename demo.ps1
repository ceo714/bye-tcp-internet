# Bye-TCP Internet v2.0 - Demo Script (PowerShell)
param(
    [switch]$ShowCurrentSettings,
    [switch]$ApplyGaming,
    [switch]$ApplyTorrent,
    [switch]$ApplyDefault,
    [switch]$Backup,
    [switch]$Monitor,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$ConfigPath = "d:\bye-tcp-internet\config"
$BackupsPath = "d:\bye-tcp-internet\backups"

function Write-Logo {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Bye-TCP Internet v2.0 (Demo Mode)" -ForegroundColor Cyan
    Write-Host "  TCP/IP Optimizer for Windows" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Test-Admin {
    $user = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($user)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-TcpSettings {
    Write-Host "`nCurrent TCP/IP Settings:" -ForegroundColor Yellow
    netsh int tcp show global 2>$null
}

function Backup-Settings {
    Write-Host "`nCreating backup..." -ForegroundColor Yellow
    $ts = Get-Date -Format "yyyyMMdd-HHmmss"
    $bf = Join-Path $BackupsPath "backup-$ts.reg"
    if (-not (Test-Path $BackupsPath)) { New-Item -ItemType Directory -Path $BackupsPath | Out-Null }
    try {
        reg export "HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters" $bf 2>$null
        if (Test-Path $bf) { Write-Host "Backup created: $bf" -ForegroundColor Green }
    } catch { Write-Host "Backup failed" -ForegroundColor Red }
}

function Apply-Profile {
    param($Name, $NetSh, $Reg)
    Write-Host "`nApplying profile: $Name" -ForegroundColor Yellow
    
    foreach ($k in $NetSh.Keys) {
        Write-Host "  NetSh: $k = $($NetSh[$k])" -ForegroundColor Cyan
        netsh int tcp set global "$k=$($NetSh[$k])" 2>$null
    }
    
    if ($Reg.Count -gt 0) {
        $ifaces = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces" -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\{' }
        foreach ($k in $Reg.Keys) {
            Write-Host "  Registry: $k = $($Reg[$k])" -ForegroundColor Cyan
            foreach ($i in $ifaces) {
                $p = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($i.PSChildName)"
                New-ItemProperty -Path $p -Name $k -Value $Reg[$k] -PropertyType DWORD -Force 2>$null | Out-Null
            }
        }
    }
    Write-Host "`nProfile applied!" -ForegroundColor Green
}

function Show-Monitor {
    Write-Host "`nNetwork Status:" -ForegroundColor Yellow
    
    # Gateway
    try {
        $gw = Get-NetRoute -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty NextHop
        if ($gw) {
            $ping = Test-Connection -ComputerName $gw -Count 1 -Quiet -ErrorAction SilentlyContinue
            Write-Host "  Gateway: $gw - $(if($ping){'OK'}else{'FAIL'})" -ForegroundColor $(if($ping){'Green'}else{'Red'})
        }
    } catch {}
    
    # DNS
    try {
        [System.Net.Dns]::GetHostAddresses("dns.google") | Out-Null
        Write-Host "  DNS: OK" -ForegroundColor Green
    } catch { Write-Host "  DNS: FAIL" -ForegroundColor Red }
    
    # Processes
    Write-Host "`nMonitored Processes:" -ForegroundColor Yellow
    @("cs2.exe","csgo.exe","qbittorrent.exe","Discord.exe","chrome.exe") | ForEach-Object {
        $p = Get-Process -Name $_ -ErrorAction SilentlyContinue
        if ($p) { Write-Host "  [RUNNING] $_ (PID: $($p.Id))" -ForegroundColor Green }
        else { Write-Host "  [STOPPED] $_" -ForegroundColor DarkGray }
    }
}

function Show-Help {
    Write-Host "`nUsage: .\demo.ps1 [Option]`n"
    Write-Host "  -ShowCurrentSettings  Show current TCP/IP settings"
    Write-Host "  -ApplyGaming          Apply gaming profile (low latency)"
    Write-Host "  -ApplyTorrent         Apply torrent profile (high throughput)"
    Write-Host "  -ApplyDefault         Apply Windows default profile"
    Write-Host "  -Backup               Create backup of current settings"
    Write-Host "  -Monitor              Show network status and processes"
    Write-Host "  -Help                 Show this help`n"
}

# Main
Write-Logo

$isAdmin = Test-Admin
if (-not $isAdmin) { Write-Host "Warning: Not running as Administrator`n" -ForegroundColor Yellow }

@($ConfigPath, $BackupsPath) | ForEach-Object { if (-not (Test-Path $_)) { New-Item -ItemType Directory -Path $_ | Out-Null } }

if ($Help -or $PSBoundParameters.Count -eq 0) { Show-Help; exit 0 }
if ($ShowCurrentSettings) { Get-TcpSettings }
if ($ApplyGaming) { Apply-Profile "Gaming" @{autotuninglevel="normal";congestionprovider="ctcp";ecncapability="disabled"} @{TcpAckFrequency=1;TCPNoDelay=1;TcpDelAckTicks=0} }
if ($ApplyTorrent) { Apply-Profile "Torrent" @{autotuninglevel="experimental";congestionprovider="cubic";ecncapability="enabled"} @{TcpAckFrequency=2;TCPNoDelay=0;TcpDelAckTicks=2} }
if ($ApplyDefault) { Apply-Profile "Default" @{autotuninglevel="normal";congestionprovider="default";ecncapability="default"} @{} }
if ($Backup) { Backup-Settings }
if ($Monitor) { Show-Monitor; Get-TcpSettings }

Write-Host "`nCompleted: $(Get-Date -Format 'HH:mm:ss')`n" -ForegroundColor DarkGray
