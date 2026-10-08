# USBPal 0.2.0

- Local device nicknames, defaulting to the reported hardware maker when meaningful, with editable names and a reset button. Names persist for offline devices and appear in search, history, activity and CSV exports. No USB stack or Windows device properties are modified.
- A plain-language bottom log across all USB buses, independent of the filters above, with reconnect durations and prominent flapping summaries.
- Reduced refresh flicker: tree nodes update in place and buffered virtual tables preserve selection, sorting, scroll position and unsaved nickname edits.

Existing history and preferences are preserved. Requires Windows 10/11 x64 and .NET Framework 4.8+. Installers remain unsigned.
