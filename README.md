# Windows Backup Helper

[![Release](https://img.shields.io/github/v/release/joffrey-b/Windows-Backup-Helper)](https://github.com/joffrey-b/Windows-Backup-Helper/releases/latest)
[![License](https://img.shields.io/github/license/joffrey-b/Windows-Backup-Helper)](LICENSE)
[![Downloads](https://img.shields.io/github/downloads/joffrey-b/Windows-Backup-Helper/total)](https://github.com/joffrey-b/Windows-Backup-Helper/releases)

A Windows desktop app that backs up your files — from a Synology NAS, another network share, or
any local folder — to local or external drives, and other network shares, without you having to remember `net use` commands
or long [Robocopy](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/robocopy)
invocations.

Define reusable **jobs** once (source → destination folder pairs, each with its own options), then
run them by hand or on a schedule. Optionally verify what got copied with a SHA256 checksum
manifest and/or a FLAC audio integrity audit.

## Features

- **Jobs and folder pairs** — group related source → destination copies together and run them as
  one unit, or run a single pair on its own.
- **Dry run** — preview exactly what Robocopy would do before anything actually changes on disk.
- **Live progress** — see how many files have been copied so far while a job is running, with a
  Cancel button if you need to stop it.
- **Scheduling** — hand a job off to Windows Task Scheduler to run automatically, with no need to
  keep the app open.
- **Verification** — optional SHA256 checksum manifests (great for cold/archival backups) and a
  FLAC integrity audit for music libraries, catching corrupt or truncated audio a plain copy
  wouldn't.
- **Exclusion rules** — skip files/folders by wildcard or regex pattern, per job or per pair.
- **Run history** — every run (manual or scheduled) is logged with its outcome; double-click to
  open Robocopy's own log file.
- **Safety first** — the app always asks for confirmation before running anything destructive
  (Mirror, Purge, or Move), and lets you delete a job or pair without losing its history.
- **Desktop notifications** when a job finishes, even if the app is minimized to the tray.
- **Credentials stay in Windows** — NAS logins are stored in Windows Credential Manager, never in
  the app's own database (see [Trust & privacy](#trust--privacy)).

## Download

Grab the latest `WindowsBackupHelper.exe` from the [Releases page](../../releases/latest).

- No installer, no admin rights, no .NET runtime to install first — it's a single, self-contained
  `.exe`. Just download and run it.
- Requires 64-bit Windows 10 or 11.
- The optional FLAC audit feature needs `flac.exe` on your `PATH` — see
  [Chocolatey's flac package](https://community.chocolatey.org/packages/flac)
  (`choco install flac`) or [xiph.org's FLAC releases](https://ftp.osuosl.org/pub/xiph/releases/flac/)
  if you want to use it. Everything else works without it.

## Getting started

1. **(Optional) Credentials tab** — add an entry if your source or destination needs a network
   login (e.g. a Synology NAS share). Skip this for local or external drives.
2. **Jobs tab** — click "Add job" and give it a name. A job is just a named container for one or
   more folder pairs that run together.
3. Inside the job, click "Add pair..." and set Source and Destination (Browse fills these in, or
   type a path directly). Pick a credential from the dropdown if that side needs one.
4. Leave Robocopy options alone to use the app-wide defaults, or override them for this job/pair
   if you know what you need to change.
5. Click "Dry run (preview)" first — it shows what Robocopy would do without touching anything on
   disk. Once you're confident, click "Run job".
6. **(Optional)** Click "Schedule..." to run the job automatically via Windows Task Scheduler.

The app's own Help tab covers this in more detail, including how Robocopy options cascade between
app/job/pair levels and what Robocopy's exit codes mean.

### Large music libraries: enable long path support

Deep `Artist/Album (Year) [Format]/Disc N/Track.flac` folder structures, combined with a NAS UNC
path prefix, commonly exceed Windows' historical 260-character path limit. If you hit path-length
errors, enable long path support once, system-wide, from an elevated PowerShell prompt (a reboot
is needed afterward):

```powershell
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" `
  -Name "LongPathsEnabled" -Value 1 -PropertyType DWORD -Force
```

## Trust & privacy

This is published as a transparent alternative to paid/freemium backup tools. These properties are
load-bearing, not just marketing claims:

- **Zero telemetry, zero crash reporting, zero data collection.** The app makes no network calls
  except the ones you explicitly configure (SMB to your own NAS, Robocopy to your own
  destinations).
- **No secret ever touches the app's own database.** NAS credentials are stored exclusively in
  Windows Credential Manager (the OS-native secure vault). The app's own database schema has no
  column capable of holding a plaintext or encrypted secret — every credential reference is a GUID
  pointer into Credential Manager, resolved at connect-time.
- **Open source, GPLv3-licensed.** Read the code; it does what this README says.

## Status

Actively used for the author's own daily NAS backups. Still pre-1.0 — if something looks wrong or
you'd like a feature, please open an issue.

## Contributing

Building from source, running the test suite, and cutting a release are covered in
[CONTRIBUTING.md](CONTRIBUTING.md).

## License

[GPLv3](LICENSE).
