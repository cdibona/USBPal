# USBPal 0.1.0

First Windows x64 release: a native tray recorder for investigating USB hub flapping.

- Searchable event history, saved parent chains, current/historical USB topology and descendant filtering.
- Connection, disconnection, enumeration, problem-code, power and relevant Windows System-log events.
- Repeated-disconnect alerts, event metrics, UTC CSV export, and visible recording status.
- USB logo and multi-resolution UP tray icon.
- Per-user installer, optional sign-in startup, GitHub release packaging and verified automatic updates.

Requires Windows 10/11 x64 and .NET Framework 4.8+. Recording runs while signed in, including when the dashboard is hidden. The installer is unsigned. History is local and preserved on uninstall. See README for capture limitations and diagnostic guidance.
