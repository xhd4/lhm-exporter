# lhm-exporter

Prometheus exporter for Windows hardware sensors via [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

Configuration and CLI flags follow [prometheus-community/windows_exporter](https://github.com/prometheus-community/windows_exporter) conventions (`config.yaml`, `--config.file`, `--web.listen-address`, `--telemetry.path`, etc.).

## Requirements

- Windows 10/11 **x64** or **arm64**
- Administrator privileges for `--install` / `--uninstall`
- [Docker](https://www.docker.com/) for cross-compiling the binary

## Build

```bash
make docker-win-x64      # dist/win-x64/lhm-exporter.exe
make docker-win-arm64    # dist/win-arm64/lhm-exporter.exe
```

## Install

Run **elevated** (Administrator):

```powershell
.\dist\win-x64\lhm-exporter.exe --install
```

Installs to `%ProgramFiles%\lhm-exporter\`, creates `config.yaml` on first install, registers service `lhm-exporter`, and adds a firewall rule.

Preview:

```powershell
lhm-exporter.exe --install --dry-run
```

Custom settings at install (written into new `config.yaml` only):

```powershell
.\dist\win-x64\lhm-exporter.exe --install --web.listen-address=:9100 --telemetry.path=/secret/metrics
```

Service binPath:

```text
"C:\Program Files\lhm-exporter\lhm-exporter.exe" --config.file="C:\Program Files\lhm-exporter\config.yaml"
```

## Uninstall

```powershell
lhm-exporter.exe --uninstall
```

## Configuration

Priority: **CLI flags > config.yaml > defaults**.

Copy the example for local development:

```powershell
copy config.yaml.example config.yaml
```

Show resolved configuration:

```powershell
lhm-exporter.exe --config.file=config.yaml --print-config
```

### config.yaml example

```yaml
collectors:
  enabled: lhm

collector:
  lhm:
    sample_interval_ms: 2000
    enable_cpu: true
    enable_gpu: true

log:
  level: warn
  file: C:\ProgramData\lhm-exporter\logs\exporter.log

telemetry:
  path: /metrics

web:
  listen-address: :9182
```

### Global flags (windows_exporter-compatible)

| Flag | Default | Description |
|------|---------|-------------|
| `--config.file` | `./config.yaml` | YAML configuration file |
| `--web.listen-address` | `:9182` | Listen address |
| `--telemetry.path` | `/metrics` | Full metrics URL path |
| `--log.file` | ProgramData log path | `stdout`, `stderr`, or file path |
| `--log.level` | `warn` | `debug`, `info`, `warn`, `error` |
| `--scrape.timeout-margin` | `0.5` | Reserved for future scrape timeout handling |
| `--debug.enabled` | `false` | Debug mode |
| `--collectors.enabled` | `lhm` | Enabled collectors (`lhm` or `[defaults]`) |

### Collector `lhm` flags

| Flag | Description |
|------|-------------|
| `--collector.lhm.sample-interval-ms` | Sensor poll interval (ms, min 250) |
| `--collector.lhm.enable-cpu` | Enable CPU sensors |
| `--collector.lhm.enable-gpu` | Enable GPU sensors |
| `--collector.lhm.enable-motherboard` | Enable motherboard sensors |
| `--collector.lhm.enable-memory` | Enable memory sensors |
| `--collector.lhm.enable-storage` | Enable storage sensors |
| `--collector.lhm.enable-network` | Enable network sensors |
| `--collector.lhm.enable-controller` | Enable controller sensors |
| `--collector.lhm.sensor-allowlist` | Regex filter (allow) |
| `--collector.lhm.sensor-denylist` | Regex filter (deny) |
| `--collector.lhm.debug-metrics` | Export debug metrics |

Run `lhm-exporter.exe --help` for the full list.

## Endpoints

| Path | Description |
|------|-------------|
| `/health` | `200 ok` or `503` if sampler is unhealthy |
| `/version` | Build version |
| `telemetry.path` | Prometheus metrics (default `/metrics`) |

## Prometheus example

```yaml
scrape_configs:
  - job_name: lhm-exporter
    static_configs:
      - targets: ["windows-host:9182"]
    metrics_path: /metrics
```

With custom metrics path:

```yaml
metrics_path: /secret/metrics
```

## Migration from old config.env

| Old | New |
|-----|-----|
| `LISTEN_ADDR=0.0.0.0:9100` | `web.listen-address: :9100` |
| `URL_SECURE_PATH=x` | `telemetry.path: /x/metrics` |
| `LOG_LEVEL=WARN` | `log.level: warn` |
| `ENABLE_CPU=1` | `collector.lhm.enable_cpu: true` |
| `DEBUG_METRICS=1` | `collector.lhm.debug_metrics: true` |

## License

MIT — see [LICENSE](LICENSE). Uses LibreHardwareMonitorLib (MPL-2.0).
