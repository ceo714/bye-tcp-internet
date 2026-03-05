# Changelog

All notable changes to **bye-tcp-internet** are documented here.  
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.2.0] — 2026-03-05

### Added
- `verify.ps1` — post-apply verification script. Auto-detects active profile (UNIVERSAL / GMVELOCITY / ROLLBACK), checks all registry keys against expected values, prints live `netsh` and `Get-NetTCPSetting` output.
- `apply.bat` — interactive installer. Admin elevation check, profile selection menu, applies chosen `.reg`, prompts for reboot.
- `CHANGELOG.md` — full version history (this file).
- `EnableECNCapability=0` added to `universal.reg` (was missing; ECN causes compatibility issues on many ISP networks).
- `DefaultTTL`, `EnablePMTUBHDetect`, `Tcp1323Opts`, `QoS Psched` keys added to `gmvelocity.reg` — profile is now a complete superset of `universal.reg`.
- `MaxNegativeCacheTtl=0` added to `gmvelocity.reg` DNS section.

### Changed
- `gmvelocity.reg`: duplicate `[Tcpip\Parameters]` sections merged into one.
- `SystemResponsiveness` set to `0` in `gmvelocity.reg` (was `0x14`); `0x14` kept in `universal.reg`.
- Author tag corrected: `ceo14` → `ceo714` across all files.
- All `.reg` comments rewritten to explain *why*, not just *what*.

### Fixed
- `universal-rollback.reg` and `gmvelocity-rollback.reg` now delete **all** keys written by their respective apply files (previously `Tcp1323Opts`, `DefaultTTL`, `EnablePMTUBHDetect`, `QoS Psched` were missing from rollbacks).

---

## [1.1.0] — 2026-03-03

### Added
- `gmvelocity.reg` — Low-Latency gaming profile.
- `gmvelocity-rollback.reg` — rollback for gaming profile.
- MMCSS `Tasks\Games` priority overrides (`GPU Priority`, `Scheduling Category`, `SFIO Priority`).
- DNS aggressive mode: `NegativeCacheTime=0`, `NetFailureCacheTime=0`.
- `EnableECNCapability=0` in gaming profile.
- Rollback architecture: both profiles now ship with dedicated rollback files.

### Changed
- README restructured: added Profiles section, Technical Scope, Methodology & Safety.

---

## [1.0.0] — 2026-02-XX

### Added
- Initial release.
- `universal.reg` — baseline TCP/IP profile for Windows 10/11.
- `universal-rollback.reg` — full rollback to Windows hardcoded defaults.
- Core TCP keys: `DefaultTTL`, `EnablePMTUDiscovery`, `SackOpts`, `Tcp1323Opts`, `MaxUserPort`, `TcpTimedWaitDelay`.
- DNS tuning: `MaxNegativeCacheTtl`, `NegativeCacheTime`, `NetFailureCacheTime`.
- Multimedia throttling: `NetworkThrottlingIndex=0xFFFFFFFF`, `SystemResponsiveness=0x14`.
- QoS: `NonBestEffortLimit=0`.
