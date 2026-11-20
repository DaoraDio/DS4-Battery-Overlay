# Third-party software and names

The application, its RTSS client plugin, and the project artwork in this repository are provided under the [MIT license](LICENSE).

RivaTuner Statistics Server (RTSS), MSI Afterburner, Windows, and the .NET Framework are external software with their own terms. Their installers, executables, SDK source files, and game-hooking DLLs are not included in this repository or its release packages. Install RTSS and MSI Afterburner separately from the official distribution.

`DS4PCStats.dll` is this project's own RTSS client plugin. It is built from `src/RTSSPlugin/DS4PCStats.cpp`; it is not `RTSSHooks.dll`. The implementation interoperates with RTSS and Afterburner through their shared-memory interfaces. The build uses the Windows SDK and the Microsoft C/C++ runtime supplied by the developer's build tools.

PlayStation, DualShock, Sony, MSI, and Microsoft names belong to their respective owners. This project is independent and is not affiliated with or endorsed by them. Interface screenshots show this application's own UI with demonstration readings.
