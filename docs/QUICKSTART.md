# DS4 Battery Overlay — Version 1

## Install

1. Install RivaTuner Statistics Server (RTSS). MSI Afterburner is optional; you can also install RTSS through its installer: https://www.msi.com/Landing/afterburner
2. Extract this ZIP to a permanent folder and run DS4-Battery-Overlay.exe. Windows 10/11 x64 with .NET Framework 4.8 is required.
3. Quit RTSS and, if installed, MSI Afterburner completely. Open How to Setup in this app and click Install / update RTSS plugin. Accept Windows' administrator prompt.
4. Start RTSS, open Setup > Plugins, enable DS4PCStats.dll and click OK.
5. If you use MSI Afterburner stats, select the plugin and open Setup to assign Toggle / Show / Hide PC stats. Leave the three master OSD shortcuts in MSI Afterburner unassigned. Keep RTSS's master Show On-Screen Display on.
6. Keep RTSS and the battery app running, set RTSS's Application detection level to Low or higher for your game, and connect your controller.

The installer embeds and verifies this project's plugin, preserves existing plugin shortcuts, and backs up changed plugin/Afterburner files under %LOCALAPPDATA%\DS4OverlayRTSSTest\RTSSPluginBackups. The PCStats folder also contains the plugin and an unassigned default configuration for reference.

## Use

- Click the tray icon to open the settings. The default battery visibility shortcut is Ctrl + Shift + F8; change it on the Overlay page.
- Launch on Windows startup starts quietly in the tray. Opening the EXE manually opens settings.
- Game overlay and Desktop widget have separate settings. Turn off Click through widget to move it with the mouse.
- Select popup triggers and duration on the Popups page. Use delayed tests to check notifications in a game.
- Lightbar control is optional. Disable it here if another program should control the lightbar.
- Turn off controller is available over Bluetooth when USB power is disconnected. Holding PS for eight seconds also works while this app is running.
- Choose filters in Events and select Export events to save the matching rows.

RTSS handles game hooking. Support depends on RTSS and the game; detection exclusions or protected games can prevent overlays.

## Troubleshooting

- No in-game overlay: keep RTSS and the battery app running.
- RTSS is running but there is still no overlay: quit the battery app and run its EXE as administrator. Check that RTSS's OSD is on and application detection is Low or higher.
- Afterburner's hotkeys also hide the battery: clear Toggle / Show / Hide On-Screen Display in Afterburner > Settings > On-Screen Display. Enable DS4PCStats.dll in RTSS > Setup > Plugins, select it, open Setup and assign Toggle / Show / Hide PC stats there. Keep RTSS's master OSD on.
- MSI Afterburner is optional. Only RTSS is required for the in-game battery overlay.

## Update

Quit the app, replace DS4-Battery-Overlay.exe in the same folder, and reopen it. Settings and event history are preserved.

MIT license. RTSS must be installed separately; optional MSI Afterburner retains its own terms.
