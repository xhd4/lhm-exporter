# SKILLS.md — lhm-exporter contributor guide

Guide for humans and coding agents working on this repository.

## Purpose

Windows-only Prometheus exporter using LibreHardwareMonitorLib. Configuration follows **prometheus-community/windows_exporter** (`config.yaml`, dotted CLI flags).

## Architecture

```text
Program.cs          → CLI routing (help, install, run)
CliParser.cs        → windows_exporter-style flags
YamlConfigFlattener → YAML → flat keys (web.listen-address, …)
ConfigLoader.cs     → merge defaults ← yaml ← CLI
AppConfig.cs        → typed config + YAML serialize
ServiceInstaller.cs → --install / --uninstall
WebAppHost.cs       → Kestrel, /health, /version, telemetry.path
SensorSampler.cs    → LHM background sampling
```

Config priority: **CLI > config.yaml > defaults**.

Firewall: `firewall.enabled` / `firewall.profile` — ensured on `--install` and at runtime start; removed on `--uninstall` with the service.

## Key paths

| Path | Role |
|------|------|
| `src/lhm-exporter/` | C# source and `lhm-exporter.csproj` (no bin/obj here) |
| `/tmp/lhm-exporter/bin/…` | Local `dotnet build` output (Unix; Windows: `%TEMP%\lhm-exporter\bin\`) |
| `/tmp/lhm-exporter/obj/…` | MSBuild intermediate files (Unix; Windows: `%TEMP%\lhm-exporter\obj\`) |
| `dist/{rid}/` | Docker cross-compile output (`lhm-exporter.exe`) |
| `config.yaml.example` | Committed template |
| `config.yaml` | Local overrides (gitignored) |
| `%ProgramFiles%\lhm-exporter\config.yaml` | Production config |
| `%ProgramData%\lhm-exporter\logs\` | Default logs |

## Build

Local development (output goes to `/tmp/lhm-exporter/`, not under the repo root):

```bash
dotnet build src/lhm-exporter/lhm-exporter.csproj -c Release
```

Cross-compile Windows binaries:

```bash
make docker-win-x64
make docker-win-arm64
```

## Adding a collector.lhm option

1. Add property to `LhmCollectorConfig`
2. Map flat key `collector.lhm.your-option` in `AppConfig.FromFlatDictionary` / `ToFlatDictionary`
3. Add YAML field to `YamlLhmCollector` + serialize
4. Add CLI flag in `CliParser` help and parsing (use hyphenated flat key)
5. Wire in `WebAppHost` or `SensorSampler`
6. Update `config.yaml.example` and `README.md`

Flat keys normalize `_` → `-` (YAML `sample_interval_ms` ↔ CLI `sample-interval-ms`).

## Global config keys (windows_exporter aligned)

| Flat key | CLI flag |
|----------|----------|
| `web.listen-address` | `--web.listen-address` |
| `telemetry.path` | `--telemetry.path` |
| `log.file` | `--log.file` |
| `log.level` | `--log.level` |
| `scrape.timeout-margin` | `--scrape.timeout-margin` |
| `debug.enabled` | `--debug.enabled` |
| `collectors.enabled` | `--collectors.enabled` |
| `firewall.enabled` | `--firewall.enabled` |
| `firewall.profile` | `--firewall.profile` |

Do **not** use `Environment.GetEnvironmentVariable` — always go through `AppConfig`.

## Coding conventions

- File-scoped namespaces, `sealed` classes, English comments
- Minimal dependencies (YamlDotNet for YAML only)
- Thin `Program.cs`
- Install via `lhm-exporter.exe --install` only (no service.bat)

## Do not

- Reintroduce `config.env` or `--listen-addr` / `--secure-path`
- Add Linux runtime support
- Commit `config.yaml` or `dist/`

## Release

Tag `v*` → GitHub Actions builds `lhm-exporter-win-x64.exe` and `lhm-exporter-win-arm64.exe`.
