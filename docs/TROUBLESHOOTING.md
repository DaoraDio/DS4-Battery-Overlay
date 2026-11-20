# Troubleshooting

## The game overlay does not appear

- Check that the battery app detects your controller. If disconnected behavior is set to Show nothing, no battery badge is drawn without a controller.
- Keep RTSS and the battery app running. Verify that RTSS's master Show On-Screen Display is enabled and application detection is Low or higher for the game.
- Check the battery toggle shortcut. PC-stat visibility and battery visibility are separate.
- Check RTSS's own compatibility with the game. Detection exclusions, graphics wrappers, protected games, and some presentation modes can prevent overlays. This app does not bypass game protections.
- If RTSS is running but the overlay still does not appear, quit DS4 Battery Overlay, right-click its EXE, and choose Run as administrator.

## Do I need MSI Afterburner?

No. Only RTSS is required for the in-game battery overlay. MSI Afterburner is optional and is only needed if you want its PC monitoring statistics.

## Toggling PC stats also hides the battery

In MSI Afterburner > Settings > On-Screen Display, clear the Toggle, Show and Hide On-Screen Display hotkeys. In RTSS > Setup > Plugins, enable DS4PCStats.dll, select it, and open Setup. Assign Toggle PC stats, Show PC stats and Hide PC stats there. Keep RTSS's master Show On-Screen Display on. Set the battery shortcut separately on this app's Overlay page.

## The plugin installer asks me to close programs

Quit RTSS and, if installed, MSI Afterburner completely, including their tray icons. The installer updates the plugin and configuration while neither program is writing those files. Start them again after installation, then enable the plugin in RTSS.

## Does Windows startup open the settings window?

No. Launch on Windows startup starts the app quietly in the tray. Click the tray icon to open settings. Running the EXE manually opens settings normally. Existing enabled startup entries for the same executable are updated automatically when you run the updated app.

## The controller is missing or updates pause

Check Bluetooth/USB connectivity. Software that hides the physical controller or takes exclusive access may prevent this app from reading it. A stale reading is shown as paused rather than treated as a fresh battery update. The Bluetooth Turn off controller action performs a full disconnect when available; it is disabled while USB power is connected.

## The percentage changes when I plug in USB

The controller supplies coarse and transport-dependent status levels. USB and unplugged readings use different scales and the USB report includes a separate Full state. See the mapping in the main README. The app cannot infer a precise battery capacity from those reports.

## Steam or another app changes the lightbar

This app does not lock ownership of the controller's LED. Another program can overwrite the color. Disable its lightbar control, or disable this app's lightbar control and let the other program manage it.

## I cannot drag the desktop widget

Turn off Click through widget in Overlay > Desktop widget. The widget deliberately sits behind ordinary application windows, so move it on an exposed area of the desktop.

## The build fails

Install the C++ x86/x64 toolset and a Windows SDK in Visual Studio Build Tools. Confirm that the .NET Framework compiler exists. Run the build from Windows PowerShell or PowerShell on Windows. Test failures include reports under `artifacts/app` and native test output in the terminal.

## Reporting a problem

Include Version 1 (or the sidebar version), Windows and RTSS versions, Bluetooth/USB connection type, what you expected, and steps to reproduce. For overlay issues, add the game and graphics API if known. Review screenshots and exported events for personal information before sharing them.
