# bye-tcp-internet

**Industrial-grade TCP/IP baseline profile for modern Windows 10/11 networking stacks.**

No legacy tweaks. No placebo. No deprecated parameters.

---

## Philosophy: Minimal • Measurable • Reversible

- **Minimal** — only keys read by `tcpip.sys` on current builds. No XP/Vista/7-era garbage.
- **Measurable** — every change is verifiable via `netsh` and `Get-NetTCPSetting`. Effect is technical, not subjective.
- **Reversible** — `rollback.reg` deletes all overrides, restoring Windows hardcoded defaults. No files are modified.

---

## Profiles

Choose **one** profile depending on your use case.

### `universal.reg` — Baseline

Universal profile for workstations and servers. Removes artificial system limits.

| Key | Value | Effect |
|-----|-------|--------|
| `DefaultTTL` | 64 | Standard modern TTL (matches Linux/BSD defaults) |
| `EnablePMTUDiscovery` | 1 | Avoids IP fragmentation on modern links |
| `EnablePMTUBHDetect` | 0 | Disables slow black hole detection |
| `SackOpts` | 1 | Selective ACK — efficient loss recovery |
| `Tcp1323Opts` | 1 | Window Scaling only ¹ |
| `MaxUserPort` | 65534 | Expanded ephemeral port range |
| `TcpTimedWaitDelay` | 30 | TIME_WAIT reduced: 240s → 30s |
| `EnableECNCapability` | 0 | ECN disabled — ISP compatibility ² |
| `NetworkThrottlingIndex` | 0xFFFFFFFF | Removes multimedia packet throttle |
| `SystemResponsiveness` | 20 | 20% reserved for background tasks |
| `NonBestEffortLimit` | 0 | Removes QoS bandwidth reservation |
| `NegativeCacheTime` | 60s | Balanced DNS negative cache |
| `NetFailureCacheTime` | 30s | Retry failed DNS lookups sooner |

> ¹ **`Tcp1323Opts` explained:** Value `1` enables Window Scaling only. Value `3` also enables TCP Timestamps, adding 12 bytes overhead per segment. For most modern networks, Window Scaling alone is sufficient. On high-BDP links (satellite, intercontinental) consider value `3`.

> ² **ECN note:** Theoretically beneficial but widely mishandled by ISP equipment and home routers, causing connection failures or latency spikes. Disabled for compatibility.

### `gmvelocity.reg` — Low-Latency Gaming

Full superset of `universal.reg`. All baseline keys plus gaming-specific additions:

| Key | Value | Effect |
|-----|-------|--------|
| `SystemResponsiveness` | 0 | All CPU time available to foreground |
| `Tasks\Games GPU Priority` | 8 | Higher GPU scheduling priority |
| `Tasks\Games Priority` | 6 | Higher MMCSS task priority |
| `Tasks\Games Scheduling Category` | High | Kernel scheduler category |
| `Tasks\Games SFIO Priority` | High | Storage I/O priority for game tasks |
| `NegativeCacheTime` | 0 | Zero DNS negative cache (instant retry) |
| `NetFailureCacheTime` | 0 | Zero DNS failure cache |
| `MaxNegativeCacheTtl` | 0 | Override TTL for negative DNS responses |

---

## What is NOT changed

Intentionally left untouched to preserve stack stability:

- **Autotuning** — not disabled. Dynamic window management in Windows 11 is effective by default.
- **Congestion Control** — CUBIC remains the default provider.
- **TCPNoDelay** — not set globally. Belongs at the application level.
- **Legacy Keys** — no `TcpWindowSize` or other Vista-era parameters.

---

## Before / After

After applying and rebooting, run `netsh int tcp show global`. Key lines to check:

```
ECN Capability                      : disabled       ✓ set by profile
RFC 1323 Timestamps                 : disabled       ✓ Tcp1323Opts=1 (WS only, TS off)
Receive Window Auto-Tuning Level    : normal         — NOT touched (AutoTuning preserved)
Add-On Congestion Control Provider  : cubic          — NOT touched
```

PowerShell:

```powershell
Get-NetTCPSetting -SettingName Internet
```

Or use the included `verify.ps1` for a full automated check with PASS/FAIL output.

---

## Usage

### 1. Apply

Option A — manual:
1. Double-click the chosen `.reg` file → Run as Administrator
2. **Reboot**

Option B — interactive:
```
apply.bat   (run as Administrator)
```

### 2. Verify

```powershell
# Quick check
netsh int tcp show global

# Full automated verification
powershell -ExecutionPolicy Bypass -File verify.ps1
```

### 3. Rollback

Run the corresponding `rollback.reg` and reboot.

| Applied | Rollback |
|---------|----------|
| `universal.reg` | `universal-rollback.reg` |
| `gmvelocity.reg` | `gmvelocity-rollback.reg` |

---

## Compatibility

| OS | Status |
|----|--------|
| Windows 10 22H2+ | ✅ Supported |
| Windows 11 (all builds) | ✅ Supported |
| Windows Server 2022/2025 | ✅ Supported |
| Windows 10 < 22H2 | ⚠️ Not tested |
| Windows 7 / 8 / 8.1 | ❌ Not supported |

---

## Files

```
bye-tcp-internet/
├── universal.reg              # Baseline profile
├── universal-rollback.reg     # Rollback for universal
├── gmvelocity.reg             # Low-latency gaming profile
├── gmvelocity-rollback.reg    # Rollback for gmvelocity
├── apply.bat                  # Interactive installer
├── verify.ps1                 # Post-apply verification
├── CHANGELOG.md               # Version history
└── LICENSE
```

---

## Methodology & Safety

- Changes remove only client-side limits. Physical latency (RTT) is not affected by registry tweaks.
- Registry Overrides only — no binary patching, no driver replacement.
- Based on `tcpip.sys` behavior analysis (2025–2026 builds).

---
---

## RU — Русская документация

### Что это

**bye-tcp-internet** — верифицированный набор настроек реестра для сетевого стека `tcpip.sys`. Цель — убрать искусственные ограничения Windows без устаревших и «плацебо» методов.

### Профили

**`universal.reg`** — универсальный baseline для рабочих станций и серверов. Расширяет диапазон портов, ускоряет освобождение TIME_WAIT, активирует PMTU и SACK, отключает сетевой дроссель и резервирование QoS.

**`gmvelocity.reg`** — игровой профиль с низкой задержкой. Включает всё из universal плюс: повышение приоритета MMCSS для игр, агрессивный DNS (нулевой кэш отказов), нулевой SystemResponsiveness.

### Установка

1. Запустить нужный `.reg` от имени администратора  
   *(или `apply.bat` для интерактивного меню)*
2. **Перезагрузить систему**

### Проверка

```powershell
# Быстрая
netsh int tcp show global

# Полная автоматическая
powershell -ExecutionPolicy Bypass -File verify.ps1
```

### Откат

Запустить соответствующий `rollback.reg` → перезагрузить ПК. Все ключи удаляются, стек возвращается к заводским значениям.

### Заметки по ключам

**`Tcp1323Opts=1`** — включает только Window Scaling. Значение `3` дополнительно активирует TCP Timestamps (+12 байт на сегмент). Для большинства сетей достаточно `1`. На высоко-BDP каналах (спутник, межконтинентальные линки) можно поставить `3`.

**`EnableECNCapability=0`** — ECN отключён из-за плохой поддержки на оборудовании ISP и домашних роутерах. Включение может вызвать обрывы соединений.

### Что НЕ меняется

- AutoTuning — не отключается
- Congestion Control (CUBIC) — не меняется  
- TCPNoDelay — не навязывается глобально
- Легаси-ключи эпохи XP/Vista — отсутствуют

---

## Author

**ceo714** — [GitHub](https://github.com/ceo714)

## License

MIT
