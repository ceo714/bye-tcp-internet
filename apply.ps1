# Bye-TCP Internet v2.0 - Quick Apply Script
param(
    [ValidateSet("gaming", "torrent", "default", "streaming", "web")]
    [string]$Profile = "gaming",
    [switch]$NoConfirm
)

$ErrorActionPreference = "Stop"

Write-Host "========================================="
Write-Host "  Bye-TCP Internet v2.0 - Quick Apply"
Write-Host "========================================="
Write-Host ""

function Test-Admin {
    $user = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($user)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$isAdmin = Test-Admin
if (-not $isAdmin) {
    Write-Host "WARNING: Not running as Administrator!" -ForegroundColor Yellow
    Write-Host "Some settings require elevated privileges."
    Write-Host ""
}

# Profile definitions
$profiles = @{
    gaming = @{
        Name = "Gaming (Low Latency)"
        NetSh = @{ autotuninglevel="normal"; congestionprovider="ctcp"; ecncapability="disabled" }
        Registry = @{ TcpAckFrequency=1; TCPNoDelay=1; TcpDelAckTicks=0 }
    }
    torrent = @{
        Name = "Torrent (High Throughput)"
        NetSh = @{ autotuninglevel="experimental"; congestionprovider="cubic"; ecncapability="enabled" }
        Registry = @{ TcpAckFrequency=2; TCPNoDelay=0; TcpDelAckTicks=2 }
    }
    streaming = @{
        Name = "Streaming"
        NetSh = @{ autotuninglevel="normal"; congestionprovider="ctcp"; ecncapability="enabled" }
        Registry = @{ TcpAckFrequency=1; TCPNoDelay=1; TcpDelAckTicks=1 }
    }
    web = @{
        Name = "Web Browsing"
        NetSh = @{ autotuninglevel="normal"; congestionprovider="default"; ecncapability="enabled" }
        Registry = @{ TcpAckFrequency=2; TCPNoDelay=1; TcpDelAckTicks=1 }
    }
    default = @{
        Name = "Windows Default"
        NetSh = @{ autotuninglevel="normal"; congestionprovider="default"; ecncapability="default" }
        Registry = @{}
    }
}

$profileData = $profiles[$Profile]

Write-Host "Profile: $($profileData.Name)" -ForegroundColor Cyan
Write-Host "========================================="

# Apply NetSh settings
Write-Host "`nApplying NetSh settings:"
foreach ($key in $profileData.NetSh.Keys) {
    $value = $profileData.NetSh[$key]
    Write-Host "  netsh int tcp set global $key=$value" -ForegroundColor Gray
    netsh int tcp set global "$key=$value" 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK]" -ForegroundColor Green
    } else {
        Write-Host "  [Requires Admin]" -ForegroundColor Yellow
    }
}

# Apply Registry settings
if ($profileData.Registry.Count -gt 0) {
    Write-Host "`nApplying Registry settings:"
    $ifaces = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces" -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\{' }
    
    foreach ($key in $profileData.Registry.Keys) {
        $value = $profileData.Registry[$key]
        Write-Host "  $key = $value" -ForegroundColor Gray
        foreach ($iface in $ifaces) {
            $path = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($iface.PSChildName)"
            New-ItemProperty -Path $path -Name $key -Value $value -PropertyType DWORD -Force 2>$null | Out-Null
        }
        Write-Host "  [Applied to $($ifaces.Count) interfaces]" -ForegroundColor Green
    }
}

Write-Host "`n========================================="
Write-Host "Profile applied successfully!" -ForegroundColor Green
Write-Host "Some settings may require a reboot."
Write-Host ""

# Show current settings
Write-Host "Current TCP/IP settings:"
netsh int tcp show global 2>$null | Select-Object -First 12
