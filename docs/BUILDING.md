# Building and publishing

## Tools

- Windows x64, Windows PowerShell 5.1 or PowerShell 7.
- Visual Studio 2022 or Build Tools 2022: Desktop development with C++, MSVC x86/x64 tools, Windows SDK.
- .NET Framework 4.8. The build uses the system `Framework64\v4.0.30319\csc.exe` compiler and Framework references.

Visual Studio is found with `vswhere.exe`. No paths belonging to the original developer are needed.

```powershell
.\Build.ps1                    # compile both components
.\Build.ps1 -RunTests          # also run managed and native tests
.\Build.ps1 -RunTests -Package # also create the release ZIP and SHA-256 checksum
```

The RTSS client plugin targets x86 because RTSS loads it in its own process. The managed application targets x64. Game bitness is handled by RTSS, independently of the battery app.

Every build hashes its freshly built plugin and writes a generated copy of `RtssPluginInstaller.cs` under `artifacts/generated`. The application embeds and validates that payload. This allows compiler versions to produce different DLL bytes without weakening installation checks or modifying tracked files.

Build outputs are ignored by Git. No proprietary RTSS/Afterburner binaries are copied into the package. The native plugin uses the static MSVC runtime (`/MT`).

## Source layout

```text
src/App/             C# application, rendering, HID, settings, UI and diagnostics
src/RTSSPlugin/      C++ RTSS client plugin and safe, unassigned default hotkeys
assets/              Application icon, brand mark and popup controller artwork
tests/native/       Plugin shared-memory, visibility and shortcut regression tests
scripts/            Native build, test runner and documentation renderer
docs/images/         Rendered interface and artwork previews
.github/workflows/  Windows build and test automation
```

The checked-in C# files are the final Version 1 sources. There is no source rewriting from older UI templates or dependency on the original development folders. Some compatibility types retain names from the earlier Vulkan/OpenGL implementation; the normal Version 1 startup uses `RtssTestSession` and RTSS. Old native renderers and experimental applications are not part of this release.

## Tests

`-RunTests` runs the application's existing self-test entry points for appearance, battery mapping, power-off eligibility, PS-button hold timing, event export, tray-only startup and registration migration, RTSS output, widget behavior, and settings-window lifecycle. The C++ tests use dedicated fake RTSS/Afterburner mappings and verify that the plugin leaves foreign output intact.

The appearance/RTSS/widget checks use Windows UI facilities, and some checks temporarily exercise then restore preferences. Run them in a normal Windows session with no simultaneous settings edits. They do not launch games, install plugins, modify Afterburner configuration, or write controller output reports. They supplement, rather than replace, testing with a controller and a game.

GitHub Actions passes `-SkipDesktopTests` because hosted runners do not provide a normal user desktop. It runs the appearance, battery, power, PS-hold, export, startup, and native plugin checks. Run the full default suite locally to also verify RTSS/widget desktop integration.

The workflow uses GitHub's official [checkout](https://github.com/actions/checkout) and [upload-artifact](https://github.com/actions/upload-artifact) actions.

Native test results are printed to the terminal. Managed reports are written beside the test executable in `artifacts/app`; failures return a nonzero exit code and fail the build. A successful build by itself does not install or start the app.

## Documentation images

After building:

```powershell
.\scripts\Render-Docs.ps1
```

This renders the actual application UI with demonstration values, plus battery and notification artwork using the application's own renderers. It does not connect to a controller, hook a game, or install anything. Generated images replace `docs/images/*.png`; inspect them before committing.

## Publish to GitHub

1. Create an empty GitHub repository named `DS4-Battery-Overlay`. Do not add GitHub's starter README or license; these files are already included here.
2. Open a terminal in this folder:

   ```powershell
   git init
   git add .
   git commit -m "Release DS4 Battery Overlay Version 1"
   git branch -M main
   git remote add origin https://github.com/YOUR-USERNAME/DS4-Battery-Overlay.git
   git push -u origin main
   ```

3. Replace `YOUR-USERNAME` with your GitHub account name. GitHub Actions will build and test the source and attach a ZIP to its workflow run.
4. Create a GitHub Release with tag `v1.0` and attach `dist/DS4-Battery-Overlay-v1.0-win-x64.zip` and its `.sha256` file. The `dist` folder is deliberately ignored by Git; binaries belong in Releases.

The supplied repository snapshot contains no `.git` history, credentials, controller captures, event history, personal configuration, or experimental release archives.

## Versioning

`src/App/AssemblyInfo.cs` is the version source: `Version` controls the product/package version, `DisplayVersion` controls the sidebar label, and `FileVersion` controls Windows file metadata. Keep them consistent when making a release. The executable remains named `DS4-Battery-Overlay.exe` so updates can replace it in place.
