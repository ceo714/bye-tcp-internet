# ╔═══════════════════════════════════════════════════════════╗
# ║     Bye-TCP Internet v2.0 — Quick Apply Script            ║
# ║  Быстрое применение профилей TCP/IP оптимизации           ║
# ╚═══════════════════════════════════════════════════════════╝

param(
    [ValidateSet("gaming", "torrent", "default", "streaming", "web")]
    [string]$Profile = "gaming",
    [switch]$NoConfirm
)

$ErrorActionPreference = "Stop"

function Write-Header {
    Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║        Bye-TCP Internet v2.0 — Quick Apply                ║" -ForegroundColor Cyan
    Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
}

function Test-Admin {
    $user = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($user)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-Profile {
    param($Name)
    switch ($Name) {
        "gaming" {
            return @{
                Name = "Gaming (Low Latency)"
                NetSh = @{
                    autotuninglevel = "normal"
                    congestionprovider = "ctcp"
                    ecncapability = "disabled"
                }
                Registry = @{
                    TcpAckFrequency = 1
                    TCPNoDelay = 1
                    TcpDelAckTicks = 0
                }
            }
        }
        "torrent" {
            return @{
                Name = "Torrent (High Throughput)"
                NetSh = @{
                    autotuninglevel = "experimental"
                    congestionprovider = "cubic"
                    ecncapability = "enabled"
                }
                Registry = @{
                    TcpAckFrequency = 2
                    TCPNoDelay = 0
                    TcpDelAckTicks = 2
                }
            }
        }
        "streaming" {
            return @{
                Name = "Streaming"
                NetSh = @{
                    autotuninglevel = "normal"
                    congestionprovider = "ctcp"
                    ecncapability = "enabled"
                }
                Registry = @{
                    TcpAckFrequency = 1
                    TCPNoDelay = 1
                    TcpDelAckTicks = 1
                }
            }
        }
        "web" {
            return @{
                Name = "Web Browsing"
                NetSh = @{
                    autotuninglevel = "normal"
                    congestionprovider = "default"
                    ecncapability = "enabled"
                }
                Registry = @{
                    TcpAckFrequency = 2
                    TCPNoDelay = 1
                    TcpDelAckTicks = 1
                }
            }
        }
        "default" {
            return @{
                Name = "Windows Default"
                NetSh = @{
                    autotuninglevel = "normal"
                    congestionprovider = "default"
                    ecncapability = "default"
                }
                Registry = @{}
            }
        }
    }
}

function Apply-Profile {
    param($ProfileData)
    
    Write-Host "`n📋 Профиль: $($ProfileData.Name)" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor DarkGray
    
    # NetSh настройки
    Write-Host "`n🌐 NetSh команды:" -ForegroundColor Cyan
    foreach ($key in $ProfileData.NetSh.Keys) {
        $value = $ProfileData.NetSh[$key]
        Write-Host "   int tcp set global $key=$value" -ForegroundColor Gray
        
        $result = netsh int tcp set global "$key=$value" 2>&1
        if ($LASTEXITCODE -eq 0 -or $result -like "*Ok*") {
            Write-Host "   ✓ OK" -ForegroundColor Green
        } else {
            Write-Host "   ⚠ Требуется Administrator" -ForegroundColor Yellow
        }
    }
    
    # Registry настройки
    if ($ProfileData.Registry.Count -gt 0) {
        Write-Host "`n📝 Реестр:" -ForegroundColor Cyan
        
        $ifaces = Get-ChildItem "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces" -ErrorAction SilentlyContinue | 
            Where-Object { $_.Name -match '^\{' }
        
        foreach ($key in $ProfileData.Registry.Keys) {
            $value = $ProfileData.Registry[$key]
            Write-Host "   $key = $value" -ForegroundColor Gray
            
            foreach ($iface in $ifaces) {
                $path = "Registry::HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($iface.PSChildName)"
                New-ItemProperty -Path $path -Name $key -Value $value -PropertyType DWORD -Force 2>$null | Out-Null
            }
            Write-Host "   ✓ Применено к $($ifaces.Count) интерфейсам" -ForegroundColor Green
        }
    }
    
    Write-Host "`n═══════════════════════════════════════════════════════════" -ForegroundColor DarkGray
    Write-Host "✅ Профиль применен!" -ForegroundColor Green
    Write-Host "`n⚠️  Для применения некоторых настроек может потребоваться перезагрузка." -ForegroundColor Yellow
}

# Main
Write-Header

$isAdmin = Test-Admin
if (-not $isAdmin) {
    Write-Host "⚠️  WARNING: Not running as Administrator!" -ForegroundColor Yellow
    Write-Host "   Some settings require elevated privileges.`n" -ForegroundColor Yellow
}

if (-not $NoConfirm) {
    Write-Host "Применить профиль '$Profile'? (y/n): " -NoNewline
    $response = Read-Host
    if ($response -ne 'y' -and $response -ne 'Y') {
        Write-Host "Отменено." -ForegroundColor Gray
        exit 0
    }
}

$profileData = Get-Profile $Profile
Apply-Profile $profileData

Write-Host "`nТекущие настройки:" -ForegroundColor Cyan
netsh int tcp show global 2>$null | Select-Object -First 15
