# HideVolumeOSD 1.5 (modernised fork of 1.4)

Goal: make HideVolumeOSD start reliably with Windows 10 22H2 and Windows 11.

## Startup problems fixed
* **App no longer quits when the OSD window isn't there yet.** Right after logon Explorer / the audio
  stack usually aren't ready. Old versions retried for a short time and then exited silently
  (`Program.InitFailed`). Now the tray icon stays and the program keeps looking in the background
  (fast for 5 min, then slowly), and applies the remembered hide/show state as soon as the window appears.
* Backoff bug fixed: `1000*(count^2)` is XOR in C# (sleeps of 3,0,1,6,7,... s), not a square.
* Crash at logon fixed: the audio COM call (`getVolume`) threw when no audio endpoint was ready yet and
  killed the app. Exceptions are now caught and written to the log.
* The OSD is provoked with **mute toggled twice** instead of volume up/down, so the volume never drifts
  (e.g. 100% -> 98% after each start) and the mute state is unchanged.
* Watchdog: if Explorer restarts / Windows re-creates the OSD window (updates, long uptime) or restores it,
  the window is found and hidden again.
* Candidate selection: on Windows 11 several look-alike `XamlExplorerHostIslandWindow`s exist; the one with
  a real size that is visible is preferred. If the strict window-title match fails, a relaxed match is tried.
* `-hide` / `-show` command line mode no longer hangs (volume thread is now a background thread) and waits
  up to 60 s for the OSD window.

## Autostart
* New tray menu entry **Start with Windows** (per-user `HKCU\...\Run`, no admin rights). Also visible and
  switchable in Task Manager > Startup apps. Use this instead of copying files into `shell:startup`.

## Less "suspicious" behaviour for virus scanners
* The global low-level keyboard hook is now installed **only** while *Settings > volume in system tray*
  is on (it was always installed before).
* Hook callback no longer touches `lParam` when `nCode < 0`, and never lets exceptions escape.

## Diagnostics
* Log file: `%LOCALAPPDATA%\HideVolumeOSD\HideVolumeOSD.log` (tray menu > *Open log file*).
* Unhandled exceptions are logged instead of silently killing the app.

## Misc
* Retargeted to .NET Framework 4.8 (present in Windows 10 22H2 and Windows 11).
* Empty "offset" box in Settings no longer throws.
* `build.cmd` builds the solution from a normal command prompt.
