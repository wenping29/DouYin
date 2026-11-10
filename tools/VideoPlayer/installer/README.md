# Windows installer

The installer targets 64-bit Windows 10 version 1809 (build 17763) and later,
including Windows 11. Windows 7 is not supported because the application targets
.NET 10. The installer uses a self-contained .NET deployment, so users do not
need to install the .NET runtime separately.

Install Inno Setup 6, then run this script from the repository root or any
working directory:

```powershell
.\installer\build-installer.ps1
```

The script publishes the x64 application and creates
`artifacts\installer\VideoPlayer-Setup-<version>-win-x64.exe`.

Each run publishes into a fresh `artifacts\publish\win-x64-<timestamp>` directory
and passes it to Inno Setup via `/DPublishDir`. Reusing an output directory that
already contains many files triggers a .NET 10 MSBuild `CopyParallel` stack
overflow; the process can still return exit code 0 while silently packaging stale
binaries, so the script validates both the main DLL and the file count before
compiling. Older publish directories are left in place and can be removed
manually.

Application logs are written to `%APPDATA%\VideoPlayer\logs\`:
`app-yyyyMMdd.log` holds full diagnostics, `error.log` holds errors only
(rolls over at 2 MB, daily logs are kept for 7 days). The folder can be opened
from Settings > About > Open log directory.

The setup installs to `Program Files\VPlayer`, creates Start Menu shortcuts,
offers a checked-by-default desktop shortcut, and registers an uninstaller
visible in Windows Settings/Control Panel. After installation, an optional
checked-by-default action registers VideoPlayer as an app capable of opening
supported video formats and opens Windows Default Apps settings. Windows
requires the user to confirm the default-app choice there.
