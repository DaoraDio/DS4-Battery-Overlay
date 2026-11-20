# Architecture

## Controller data

`DS4Studio.cs` manages device arrival/removal and selects the active controller. `Ds4Input.cs` handles Windows HID reads and transport details; raw input supplements background reads. `Ds4BatteryScale.cs` maps the battery status byte to display estimates. The percentage is inferred from a coarse controller reading rather than measured by Windows as an exact charge percentage.

`ControllerSettings.cs` contains appearance and lightbar settings and output reporting. Lightbar writes are optional. `Ds4Power.cs` resolves the selected Bluetooth device and requests a disconnect without unpairing it. `Ds4PowerHold.cs` implements the eight-second PS hold. USB power explicitly disables the Bluetooth power-off action, including when a controller exposes USB and Bluetooth endpoints simultaneously.

## Rendering

`OverlayLayout.cs`, `DS4Studio.cs`'s battery renderer, and `PopupControllerIcon.cs` draw smooth badge, icon and notification artwork. `RtssOutput.cs` writes image atlases and RTSS text markup to an owned shared-memory slot. RTSS loads and draws those images in games. `DesktopBatteryWidget.cs` supplies the independent desktop widget; `DesktopNotification.cs` supplies Windows popups.

Game popups are selected when RTSS reports the foreground process as a hooked graphics application. Windows popups are selected otherwise. There is no general-purpose injector in the application. Graphics API and protected-game compatibility follow RTSS's own capabilities and configuration.

## Separate PC-stat visibility

`src/RTSSPlugin/DS4PCStats.cpp` is an x86 client plugin loaded by RTSS. It reads Afterburner's redirected live output, including formatting and graph objects, and republishes it in a dedicated slot. The plugin's Toggle/Show/Hide shortcuts control that slot. The battery has a separate slot and shortcut, and RTSS's master visibility remains enabled.

The plugin does not replace RTSS's game-hooking libraries. Its slot operations validate bounds and honor the render lock. Native tests verify that it preserves battery and other clients' output. Afterburner monitoring selections remain configured in Afterburner.

## Plugin installation

`RtssPluginInstaller.cs` runs only when explicitly invoked by the install button. It requires RTSS and Afterburner to be closed and requests elevation for the installation step. RTSS is located through its installation registry keys or a running RTSS executable; Afterburner is located through its installation registry key.

The installer:

1. Verifies the embedded plugin's SHA-256 hash.
2. Copies `DS4PCStats.dll` into RTSS's `Plugins/Client` folder.
3. Preserves configured plugin shortcuts and enables live Afterburner mirroring in its configuration.
4. Adds a positioning/layer marker to Afterburner's OSD layout headers and clears its three global OSD shortcuts so the plugin can provide independent PC-stat controls.
5. Backs up changed files with a manifest under `%LOCALAPPDATA%/DS4OverlayRTSSTest/RTSSPluginBackups` before applying changes, and attempts rollback if installation fails.

It does not install RTSS itself or copy DLLs into game directories. Enable the plugin in RTSS manually after installation.

## Local storage

| Data | Location |
| --- | --- |
| Preferences | `HKCU\Software\DS4OverlayRTSSTest` |
| Events (latest 1,000 entries) | `%LOCALAPPDATA%\DS4OverlayRTSSTest\controller-events.log` |
| Installer backups | `%LOCALAPPDATA%\DS4OverlayRTSSTest\RTSSPluginBackups` |
| RTSS-rendered artwork | RTSS's `Plugins\Client\Overlays\DS4Battery` directory |
| Plugin hotkeys | `DS4PCStats.cfg` beside the installed client plugin |
| Optional startup entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `DS4 Overlay RTSS Test` |

The internal development-era names remain to preserve existing users' data. No network service, account, telemetry, or update downloader is required by this application.
