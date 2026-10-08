# USBPal

![USBPal](assets/logo.svg)

A Windows tray companion for diagnosing USB hubs that keep disconnecting. Inspired by [PowerPal](https://github.com/cdibona/PowerPal). Windows 10/11 x64, .NET Framework 4.8+, no administrator privileges or runtime packages required.

**[Download the Windows installer](https://github.com/cdibona/USBPal/releases/latest/download/USBPal-Setup-win-x64.exe)** · [Releases and checksums](https://github.com/cdibona/USBPal/releases)

## Use it

1. Install USBPal. The installer offers recording at sign-in and opens the dashboard.
2. Find **UP** in the system tray. Double-click to open. Closing or minimizing keeps recording; **Exit and stop recording** ends it.
3. Select a controller, hub, or device in the topology tree. **Include descendants** shows events for its downstream devices, using the parent chain saved with each event.
4. Search names, instance IDs, parent IDs, manufacturer, locations, and event messages. Choose an hour, day, week, or all history, and an event type.
5. **Export CSV** exports every matching event (UTC timestamps), even when the table is capped at the newest 5,000 rows.

Disconnected devices remain in the tree for the current session and selected historical range. Details include Windows instance ID, ancestry, location/port information, manufacturer, and Windows problem code. The topology is Windows' PnP tree, including PCI/ACPI ancestors and USB composite interfaces; it is not an invented physical wiring diagram. A physical device may have several logical nodes. Devices without serial numbers can change identity when moved between ports.

## Device nicknames and all-bus activity

Select a device or hub in the tree, edit **Local nickname**, and click **Save nickname** (or press Enter). **Use default** restores the reported hardware maker when available; generic/unknown manufacturer strings fall back to the Windows device name. The original name stays alongside the nickname so devices from the same maker can be distinguished.

Nicknames apply to the tree, history table, search, all-bus activity and CSV export. They are saved by Windows instance ID in `%LOCALAPPDATA%\USBPal\device-nicknames.json`, including for offline devices. These are USBPal display labels only: **no writes to the USB stack, device firmware, drivers, or Windows device properties**. Original JSONL history is not rewritten. Moving a device without a stable serial identity to another port can require a new nickname.

The bottom log always shows the latest 24 hours across **all USB buses**, regardless of the selected device, search, event type, or time range above. It shows up to 500 recent meaningful events, omitting baseline enumeration noise. Plain-language entries explain disconnects, reconnect duration, device problems, recording gaps and power transitions. Double-click an entry to select its device. Select a device or event to read full details in the fixed panel below the activity log; floating tooltips are disabled.

The bottommost footer keeps save/update messages and recording status visible. Its release link opens the installed version's GitHub release; **App uptime** measures the current USBPal session, including time in the tray, and resets when USBPal restarts. The website link after uptime opens [halogenica.com](https://halogenica.com).

An amber **POSSIBLE FLAPPING** summary names devices with at least three logged disconnects in the past five minutes, independently of the upper filters. Historical flap alerts remain in the log after the live summary clears. Reconnection durations describe observed events and can span recording gaps; scan-derived events explicitly indicate uncertain timing.

Tree nodes update in place. Buffered virtual tables skip unchanged refreshes and preserve selection, sorting and scroll position as new events arrive. Nickname text in progress is left untouched by background refreshes.

## What gets recorded

- Configuration Manager device-instance enumeration, start and removal notifications, captured with UTC timestamps on the notification callback and processed off that callback.
- Connected/disconnected transitions, with duplicate notifications suppressed. Devices present at launch are **Observed**, not counted as new connections.
- A three-second reconciliation scan, including changes to Windows problem codes. Reconciled changes are explicitly labeled: exact transition time is unknown.
- **Flapping** alerts after three disconnects for the same instance within five minutes. The window is per running session; alerts and their context are persisted. Subsequent disconnects in that window generate further alerts.
- Matching new events from the existing Windows **System** log for Kernel-PnP, UserPnp, DriverFrameworks-UserMode, USBHUB3 and UCX, when they contain a USB instance ID or a known device ID. No diagnostic channels are enabled. Permissions/provider failures are visible in recorder status.
- Recorder start/stop and Windows suspend/resume events, to help interpret simultaneous changes.

Select a flapping hub to correlate its children's events. Simultaneous disconnects implicate an upstream path, but do not prove whether the fault is the hub, cable, power supply, controller, or driver.

This is a **per-user tray application**, like PowerPal, not a Windows service. Recording stops at sign-out, exit, and sleep. It does not collect USB packets, electrical measurements, bandwidth, USB-C power contracts, or every driver-specific diagnostic/ETW event. Very short-lived unknown devices can disappear before Windows supplies their names and parents; their USB instance notifications are retained with unresolved topology. Enumeration and reconciliation cannot guarantee detection of every electrical flap. A device that remains enumerated through a link reset may only produce a Windows diagnostic event, if its driver emits one.

## History and privacy

Daily JSONL files live in `%LOCALAPPDATA%\USBPal\Events`. Each event stores its own device metadata and ancestry, preserving the route after unplugging. Settings and downloaded updates live alongside them. No history is sent anywhere; only the GitHub updater uses the network.

History is retained until you remove it, and uninstall preserves it. Storage write failures are shown in status and queued records are retried while the process stays alive; queued records cannot survive a crash or exit during a persistent disk failure. Search skips malformed records and displays their count. Instance IDs can contain serial numbers: review exported data before sharing it.

The tree combines current topology with devices in the chosen history range. Metrics describe matching logged events, not electrical measurements or guaranteed lifetime totals. Broad history searches load the selected range into memory; reduce the range for large archives.

## Releases and automatic updates

GitHub Actions builds/tests each push to `main` and each pull request. Pushing `vMAJOR.MINOR.PATCH` builds a per-user Inno Setup installer and publishes it in GitHub Releases, with SHA-256 sidecars and a stable download alias. No separate GitHub Packages registry is needed.

Automatic updates default on; toggle them in the tray menu. USBPal checks the public repository at startup and every six hours. **Check updates** checks immediately. Only newer stable releases with the correctly named versioned installer and a GitHub SHA-256 asset digest are accepted. Downloads use HTTPS, restricted hosts, a size limit and SHA-256 verification. Verified installers run when the dashboard is hidden, then restart USBPal quietly. This briefly interrupts recording. Startup preferences and history are preserved. Failures leave recording running.

Installers are currently unsigned. SHA-256 checks verify download integrity; they do not replace publisher code signing. Private repositories are not supported by the unauthenticated updater.

## Development

```powershell
./build.ps1 -Version 0.2.1
./bin/v0.2.1/USBPal.exe --show
./package.ps1 -Version 0.2.1 -Compiler 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

Build uses the C# compiler shipped with Windows .NET Framework. Distribute both `USBPal.exe` and `USBPal.exe.config` for portable use. Inno Setup 6 is required only for packaging. `assets/make-icon.ps1` regenerates the multi-resolution UP icon; the USB logo is editable SVG.

```powershell
# Each command below exits; wait for the process when scripting tests.
./bin/v0.2.1/USBPal.exe --self-test
./bin/v0.2.1/USBPal.exe --probe C:/absolute/path/topology.json
./bin/v0.2.1/USBPal.exe --runtime-test C:/absolute/path/test-output
```

`--self-test` checks transition deduplication, baseline semantics, flapping thresholds/expiry, historical ancestry filtering, event immutability, problem changes, history recovery, UTC filtering, CSV escaping, and updater validation. Results are beside the executable. `--runtime-test` reads real hardware, checks notification registration and persistence, renders a dashboard PNG, tests close/reopen, and exits. It uses isolated history, makes no startup changes and performs no update checks. Real hardware tests are local because hosted CI runners may have no USB tree.

Before tagging a release, build and run both tests on Windows, exercise a safe spare USB device, inspect a hub's descendant history, and test the installer/update cycle. Do not disconnect storage with pending writes. Version tags are the release source of truth.

Native references: [CM_Register_Notification](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_register_notification), [CM_Get_Parent](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_get_parent), [device instance actions](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/ne-cfgmgr32-cm_notify_action).
