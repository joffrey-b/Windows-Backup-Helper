# Contributing

## Solution layout

```
src/
  WindowsBackupHelper.Core/        # Cross-platform business logic: Robocopy argument building/output
                                    #   parsing, checksum & FLAC services, exclusion resolution, SQLite
                                    #   repositories. No Windows-only APIs.
  WindowsBackupHelper.Core.Tests/  # xUnit tests for Core (run on any OS)
  WindowsBackupHelper.Win/         # Windows-only integrations: Credential Manager, SMB session
                                    #   management, Task Scheduler, process runners (robocopy.exe/flac.exe)
  WindowsBackupHelper.Win.Tests/   # xUnit tests for Win
  WindowsBackupHelper.App/         # WPF UI + headless CLI entry point
scripts/                           # Original Python tools this app's checksum/FLAC services port from
docs/WINDOWS_HANDOFF.md            # Full design/build brief this implementation follows
```

## Building and testing

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
```

`WindowsBackupHelper.Win.Tests`'s `FlacProcessRunnerTests` exercises a real `flac.exe` and will
fail if one isn't on `PATH` (CI installs it explicitly — see `.github/workflows/ci.yml`). Install
the reference FLAC tools locally too, e.g. via
[Chocolatey](https://community.chocolatey.org/packages/flac) (`choco install flac`) or by
downloading a build from [xiph.org's FLAC releases](https://ftp.osuosl.org/pub/xiph/releases/flac/)
and adding its folder to `PATH`.

## Publishing a build locally

```
dotnet publish src/WindowsBackupHelper.App/WindowsBackupHelper.App.csproj -c Release -p:PublishProfile=win-x64
```

Produces a single self-contained `WindowsBackupHelper.exe` (no .NET runtime install required
on the target machine) at `src/WindowsBackupHelper.App/bin/Release/net10.0-windows/win-x64/publish/`.

## CI

Two pipelines build/test/publish the same self-contained `win-x64` exe (via
`src/WindowsBackupHelper.App/Properties/PublishProfiles/win-x64.pubxml` — self-contained,
single-file, `PublishReadyToRun`, trimming deliberately disabled since WPF's trimming support is
still unresolved upstream):

- **`.gitlab-ci.yml`** — runs on every push, on the self-hosted Windows runner, uploading the exe
  as a pipeline artifact.
- **`.github/workflows/ci.yml`** — runs on every push/PR to `main`: a Linux job for Core's
  platform-independent tests, and a Windows job for the full build/test/publish, uploading the exe
  as a workflow artifact.

## Cutting a release

Pushing a tag matching `v*` (e.g. `v1.0.0`) triggers `.github/workflows/release.yml`, which
builds, tests, publishes the exe, and creates a GitHub Release for that tag with the exe attached
and auto-generated release notes from the commits since the previous tag.

```
git tag v1.0.0
git push origin v1.0.0
```
