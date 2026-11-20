# Contributing

Please open an issue for a bug or a substantial feature before starting a large change. Small fixes and documentation improvements can go straight to a pull request.

1. Build with `./Build.ps1 -RunTests -Package` on Windows.
2. Keep changes focused and describe the resulting behavior and validation.
3. Add regression coverage when fixing a meaningful behavior bug. Existing managed checks are in their respective classes; native plugin tests are in `tests/native/Tests.cpp`.
4. If the interface changes, regenerate and inspect documentation images with `./scripts/Render-Docs.ps1`.
5. Preserve settings compatibility and independent ownership of RTSS display slots. Keep unrelated controller, Afterburner, and RTSS settings intact.

Do not commit controller captures, event logs, personal game paths, credentials, local configurations, build products, or third-party executable files. See the `.gitignore` and [build guide](docs/BUILDING.md).

Contributions are provided under this repository's MIT license.
