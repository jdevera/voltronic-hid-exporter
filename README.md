# Voltronic HID Exporter

A small, read-only Windows service that exports Prometheus metrics from
Voltronic-QS-compatible UPS devices connected over USB HID. It was built for a
Salicru SPS One 700VA BL (`0665:5161`) that exposes telemetry on `MI_00` while
Windows continues to manage `MI_01` as a native battery.

The exporter implements only the read-only `M`, `F`, and `QS` queries. It does
not implement battery tests, beeper changes, scheduled shutdown, or output
control. Installing it does not replace a USB driver and does not remove the
UPS from Windows' battery subsystem.

## Installation

Download the `win-x64.msi` from the latest GitHub release and install it as an
administrator. The package:

- installs a self-contained executable under `Program Files`;
- registers the `VoltronicHidExporter` Windows service as `LocalSystem`;
- starts the service automatically and restarts it after failures;
- supports in-place upgrades and standard uninstall through Installed Apps;
- does not require a separately installed .NET runtime.

The MSI is currently unsigned, so Windows may show an unknown-publisher
warning. Release checksums are published alongside it.

By default, metrics listen only on `http://127.0.0.1:9199/metrics`. The MSI does
not open a firewall port. This safe default is useful for a local Prometheus
agent; central scraping requires the configuration described below and a
narrow Windows Firewall rule.

The standalone release EXE is also usable for a one-off diagnostic without
installing the service:

```powershell
.\voltronic-hid-exporter-0.1.0-win-x64.exe probe
```

Do not run `probe` while the service or another program is reading the same
HID interface.

## Configuration

The service reads optional configuration from:

```text
%ProgramData%\VoltronicHidExporter\appsettings.json
```

Print the complete defaults with:

```powershell
& "$env:ProgramFiles\Voltronic HID Exporter\voltronic-hid-exporter.exe" print-default-config
```

For example, this listens on every interface and retains seven UTC-dated raw
capture files:

```json
{
  "Exporter": {
    "ListenPrefix": "http://+:9199/",
    "RawCaptureRetentionDays": 7
  }
}
```

Restart the service after changing configuration:

```powershell
Restart-Service VoltronicHidExporter
```

When exposing the listener, restrict TCP port 9199 in Windows Firewall to the
Prometheus server. The exporter has no authentication because Prometheus
scrape endpoints are intended to be protected at the network boundary.

Raw capture is disabled by default. When enabled, daily JSONL files are stored
under `%ProgramData%\VoltronicHidExporter\capture`. Each record is opened,
appended, and closed independently so recent samples survive an unexpected
power loss. Retention deletes only exporter-owned `ups-YYYY-MM-DD.jsonl` files.
Configuration and captures are intentionally preserved when the MSI is
uninstalled.

## Endpoints and metrics

- `/metrics` returns Prometheus exposition data.
- `/health` returns HTTP 200 after a successful UPS poll and HTTP 503 when the
  device is unavailable or its last poll failed.

Metrics include communication health, voltages, output load, frequency,
battery voltage, all eight Voltronic status flags, native Windows battery
charge/runtime, power-cut count, the last power-cut timestamp, and current
outage duration. Historical storage belongs in Prometheus; the exporter has no
database.

## Compatibility

The tested device is a Salicru SPS One 700VA BL with USB identity `0665:5161`,
protocol response `H`, nine-byte HID reports, and telemetry interface `MI_00`.
The defaults reflect that hardware. Protocol dialect `V` is also accepted, but
other devices should be validated using `probe` before the service is trusted.

The status-bit interpretation follows the public
[Voltronic QS protocol documentation](https://networkupstools.org/protocols/voltronic-qs.html).
USB transport uses [HidSharp](https://github.com/IntergatedCircuits/HidSharp).

## Development

Build, test, and package with the .NET 8 SDK on Windows:

```powershell
.\scripts\build-release.ps1 -Version 0.1.0
```

The installer is built with WiX 7. Its project records acceptance of the
[WiX Open Source Maintenance Fee EULA](https://docs.firegiant.com/wix/osmf/).
Pushing a `v*` tag invokes the same repository script, then publishes its
self-contained EXE, MSI, and SHA-256 checksums as a GitHub release.

## Licence

This project is licensed under the MIT Licence. HidSharp is distributed under
the Apache License 2.0. No source code is copied from Network UPS Tools or from
device-specific reverse-engineering projects.
