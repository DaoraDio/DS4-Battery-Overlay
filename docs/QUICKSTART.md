# DS4 Battery Overlay — Version 1

## Install

1. Install MSI Afterburner with RivaTuner Statistics Server (RTSS) selected in the installer: https://www.msi.com/Landing/afterburner
2. Extract this ZIP to a permanent folder and run DS4-Battery-Overlay.exe. Windows 10/11 x64 with .NET Framework 4.8 is required.
3. Quit RTSS and MSI Afterburner completely. Open How to Setup in this app and click Install / update RTSS plugin. Accept Windows' administrator prompt.
4. Start RTSS, open Setup > Plugins, enable DS4PCStats.dll and click OK.
5. Select the plugin and open Setup to assign Toggle / Show / Hide PC stats. Leave the three master OSD shortcuts in MSI Afterburner unassigned. Keep RTSS's master Show On-Screen Display on.
6. Keep RTSS and the battery app running, set RTSS's Application detection level to Low or higher for your game, and connect your controller.

The installer embeds and verifies this project's plugin, preserves existing plugin shortcuts, and backs up changed plugin/Afterburner files under %LOCALAPPDATA%\DS4OverlayRTSSTest\RTSSPluginBackups. The PCStats folder also contains the plugin and an unassigned default configuration for reference.

## Use

- Click the tray icon to open the settings. The default battery visibility shortcut is Ctrl + Shift + F8; change it on the Overlay page.
- Game overlay and Desktop widget have separate settings. Turn off Click through widget to move it with the mouse.
- Select popup triggers and duration on the Popups page. Use delayed tests to check notifications in a game.
- Lightbar control is optional. Disable it here if another program should control the lightbar.
- Turn off controller is available over Bluetooth when USB power is disconnected. Holding PS for eight seconds also works while this app is running.
- Choose filters in Events and select Export events to save the matching rows.

RTSS handles game hooking. Support depends on RTSS and the game; detection exclusions or protected games can prevent overlays.

## Update

Quit the app, replace DS4-Battery-Overlay.exe in the same folder, and reopen it. Settings and event history are preserved.

MIT license. RTSS and MSI Afterburner must be installed separately and retain their own terms.
