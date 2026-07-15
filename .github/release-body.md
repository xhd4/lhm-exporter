## Features

First release of **lhm-exporter** — a Windows Prometheus exporter for hardware sensors powered by [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) **0.9.6**.

- windows_exporter-compatible CLI and `config.yaml` (`--web.listen-address`, `--telemetry.path`, `--config.file`, …)
- Windows service install/uninstall (`--install` / `--uninstall`) with firewall rule
- Sensor groups: CPU, GPU, motherboard, memory, storage, network, controller
- Sensor allowlist / denylist regex filters
- Endpoints: `/health`, `/version`, and configurable metrics path
- Self-contained single-file binaries for **win-x64** and **win-arm64**

## Downloads

Attach `lhm-exporter-win-x64.exe` and `lhm-exporter-win-arm64.exe` from this release. Run elevated for service install.
