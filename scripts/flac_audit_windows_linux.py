#!/usr/bin/env python3
"""
flac_audit.py — Audit a FLAC music library using `flac -t`.
Generates a Markdown report grouped by folder with a summary of errors/warnings.

Paths in the report always use forward slashes regardless of OS, making the
report cross-platform and compatible with flac_fix_warnings.py on both systems.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
REQUIREMENTS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  The `flac` command-line tool must be installed and in your PATH.

  Linux:   sudo apt install flac        (Debian/Ubuntu)
           sudo dnf install flac        (RHEL/Fedora)

  Windows: Download from https://xiph.org/flac/download.html
           or via Chocolatey: choco install flac
           Then add the flac.exe folder to your system PATH.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
USAGE (Linux)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  # Basic run — report written to flac_audit_report.md
  python3 flac_audit.py /media/nas_music/

  # Custom output file
  python3 flac_audit.py /media/nas_music/ --output my_report.md

  # Custom number of parallel workers
  python3 flac_audit.py /media/nas_music/ --workers 4

  # Only include files with errors or warnings in the per-folder tables
  python3 flac_audit.py /media/nas_music/ --errors-only

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
USAGE (Windows)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  # Basic run — report written to flac_audit_report.md
  python flac_audit.py "E:\\Storage SSD - Music"

  # Custom output file
  python flac_audit.py "E:\\Storage SSD - Music" --output my_report.md

  # Custom number of parallel workers
  python flac_audit.py "E:\\Storage SSD - Music" --workers 4

  # Only include files with errors or warnings in the per-folder tables
  python flac_audit.py "E:\\Storage SSD - Music" --errors-only

  # UNC path (network share)
  python flac_audit.py "\\\\nas\\Music"

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
OPTIONS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  --output / -o      Output Markdown file. Default: flac_audit_report.md
  --workers / -w     Number of parallel workers. Default: min(8, cpu_count).
                     Since flac -t is I/O-bound, you can go higher than your
                     CPU count when reading from a NAS over a network link.
  --errors-only      Only include files with errors or warnings in the
                     per-folder tables. OK files are still counted in the
                     summary but omitted from the detailed breakdown.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
OUTPUT
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  The Markdown report contains:
    - A global summary table (OK / Warning / Error counts)
    - A "Problems at a Glance" section listing all problematic files
    - A per-folder breakdown with one table per album/directory

  Status meanings:
    OK       -- file passed verification fully
    WARNING  -- file is readable but has no MD5 checksum in STREAMINFO
                (use flac_fix_warnings.py to fix these)
    ERROR    -- file is corrupted and could not be decoded
                (re-rip or re-download required)
"""

import argparse
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

# Force UTF-8 output on Windows so emojis and the progress bar render correctly.
# Windows terminals default to cp1252 which would break the progress bar and icons.
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if sys.stderr.encoding and sys.stderr.encoding.lower() != "utf-8":
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


# ---------------------------------------------------------------------------
# Data structures
# ---------------------------------------------------------------------------

@dataclass
class FileResult:
    path: Path
    status: str          # "ok" | "warning" | "error"
    messages: list[str] = field(default_factory=list)


# ---------------------------------------------------------------------------
# FLAC verification
# ---------------------------------------------------------------------------

def verify_flac(path: Path) -> FileResult:
    """Run `flac -t` on a single file and return a structured result."""
    try:
        proc = subprocess.run(
            ["flac", "-t", "--silent", str(path)],
            capture_output=True,
            text=True,
        )
        stderr = proc.stderr.strip()
        stdout = proc.stdout.strip()
        combined = "\n".join(filter(None, [stdout, stderr]))

        if proc.returncode != 0:
            # Collect all meaningful error lines
            messages = [
                line.strip()
                for line in combined.splitlines()
                if line.strip() and not line.strip().startswith(str(path))
            ]
            return FileResult(path=path, status="error", messages=messages or ["Unknown error"])

        # Check for MD5 warning even on exit code 0
        if "cannot check MD5" in combined or "WARNING" in combined:
            messages = [
                line.strip()
                for line in combined.splitlines()
                if line.strip() and ("WARNING" in line or "cannot check" in line)
            ]
            return FileResult(path=path, status="warning", messages=messages or ["MD5 signature unset"])

        return FileResult(path=path, status="ok")

    except FileNotFoundError:
        print(
            "ERROR: `flac` is not installed or not in PATH.\n"
            "  Linux:   sudo apt install flac\n"
            "  Windows: https://xiph.org/flac/download.html  or  choco install flac",
            file=sys.stderr,
        )
        sys.exit(1)


# ---------------------------------------------------------------------------
# Discovery
# ---------------------------------------------------------------------------

def find_flac_files(root: Path) -> list[Path]:
    """Recursively find all .flac files under root."""
    return sorted(root.rglob("*.flac"))


# ---------------------------------------------------------------------------
# Markdown report
# ---------------------------------------------------------------------------

STATUS_ICON  = {"ok": "\u2705", "warning": "\u26a0\ufe0f", "error": "\u274c"}
STATUS_LABEL = {"ok": "OK", "warning": "WARNING", "error": "ERROR"}


def relative_folder_label(file_path: Path, root: Path) -> str:
    """
    Return the folder path relative to root using forward slashes.
    e.g. 'Artist/Album' -- consistent on both Windows and Linux.
    """
    try:
        rel = file_path.parent.relative_to(root).as_posix()
        return rel if rel != "." else "(root)"
    except ValueError:
        return file_path.parent.as_posix()


def generate_markdown(results: list[FileResult], root: Path, elapsed: float) -> str:
    lines = []

    # --- Header ---
    lines.append("# FLAC Library Audit Report")
    lines.append(f"\n**Date:** {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}  ")
    lines.append(f"**Library root:** `{root}`  ")
    lines.append(f"**Duration:** {elapsed:.1f}s  ")
    lines.append(f"**Files scanned:** {len(results)}\n")

    # --- Global summary ---
    ok_count    = sum(1 for r in results if r.status == "ok")
    warn_count  = sum(1 for r in results if r.status == "warning")
    error_count = sum(1 for r in results if r.status == "error")

    lines.append("## Summary\n")
    lines.append(f"| Status | Count |")
    lines.append(f"|--------|-------|")
    lines.append(f"| {STATUS_ICON['ok']} OK       | {ok_count} |")
    lines.append(f"| {STATUS_ICON['warning']} Warning  | {warn_count} |")
    lines.append(f"| {STATUS_ICON['error']} Error    | {error_count} |")
    lines.append(f"| **Total**   | **{len(results)}** |")
    lines.append("")

    # --- Problems-only quick list ---
    problems = [r for r in results if r.status != "ok"]
    if problems:
        lines.append("## \u26a1 Problems at a Glance\n")
        for r in problems:
            icon = STATUS_ICON[r.status]
            # Always use forward slashes so flac_fix_warnings.py can parse
            # this section correctly on both Windows and Linux.
            if r.path.is_relative_to(root):
                rel = r.path.relative_to(root).as_posix()
            else:
                rel = r.path.as_posix()
            lines.append(f"- {icon} `{rel}`")
            for msg in r.messages:
                lines.append(f"  - _{msg}_")
        lines.append("")
    else:
        lines.append("## \u2705 No Problems Found\n\nAll files passed verification.\n")

    # --- Per-folder breakdown ---
    lines.append("## Results by Folder\n")

    # Group by folder
    folders: dict[str, list[FileResult]] = {}
    for r in results:
        folder = relative_folder_label(r.path, root)
        folders.setdefault(folder, []).append(r)

    for folder, folder_results in sorted(folders.items()):
        f_warn = sum(1 for r in folder_results if r.status == "warning")
        f_err  = sum(1 for r in folder_results if r.status == "error")

        # Folder heading with mini badge
        if f_err:
            badge = f"{STATUS_ICON['error']} {f_err} error(s)"
        elif f_warn:
            badge = f"{STATUS_ICON['warning']} {f_warn} warning(s)"
        else:
            badge = f"{STATUS_ICON['ok']} all OK"

        lines.append(f"### \U0001f4c1 `{folder}` -- {badge}\n")
        lines.append(f"| File | Status | Details |")
        lines.append(f"|------|--------|---------|")

        for r in sorted(folder_results, key=lambda x: x.path.name):
            icon   = STATUS_ICON[r.status]
            label  = STATUS_LABEL[r.status]
            detail = "; ".join(r.messages) if r.messages else ""
            # Escape pipe chars that would break the Markdown table
            detail = detail.replace("|", "\\|")
            lines.append(f"| `{r.path.name}` | {icon} {label} | {detail} |")

        lines.append("")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Progress display
# ---------------------------------------------------------------------------

def print_progress(done: int, total: int, last_path: Path) -> None:
    pct = done / total * 100
    bar_len = 30
    filled = int(bar_len * done / total)
    bar = "\u2588" * filled + "\u2591" * (bar_len - filled)
    name = last_path.name[:40].ljust(40)
    print(f"\r[{bar}] {pct:5.1f}%  {done}/{total}  {name}", end="", flush=True)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Audit a FLAC library and generate a Markdown report."
    )
    parser.add_argument("root", help="Root directory of the music library")
    parser.add_argument(
        "--output", "-o",
        default="flac_audit_report.md",
        help="Output Markdown file (default: flac_audit_report.md)",
    )
    parser.add_argument(
        "--workers", "-w",
        type=int,
        default=min(8, (os.cpu_count() or 4)),
        help="Number of parallel workers (default: min(8, cpu_count))",
    )
    parser.add_argument(
        "--errors-only",
        action="store_true",
        help="Only report files with errors or warnings in the per-folder tables",
    )
    args = parser.parse_args()

    root = Path(args.root).resolve()
    if not root.is_dir():
        print(f"ERROR: '{root}' is not a directory.", file=sys.stderr)
        sys.exit(1)

    print(f"\U0001f50d Scanning {root} for FLAC files\u2026")
    flac_files = find_flac_files(root)
    total = len(flac_files)

    if total == 0:
        print("No FLAC files found.")
        sys.exit(0)

    print(f"   Found {total} file(s). Starting verification with {args.workers} worker(s)\u2026\n")

    results: list[FileResult] = []
    start = datetime.now()

    with ThreadPoolExecutor(max_workers=args.workers) as executor:
        futures = {executor.submit(verify_flac, p): p for p in flac_files}
        done = 0
        for future in as_completed(futures):
            result = future.result()
            results.append(result)
            done += 1
            print_progress(done, total, result.path)

    elapsed = (datetime.now() - start).total_seconds()
    print(f"\n\n\u2705 Verification complete in {elapsed:.1f}s.")

    # Optionally filter OK files from per-folder tables
    if args.errors_only:
        report_results = [r for r in results if r.status != "ok"]
        print(f"   (--errors-only: showing {len(report_results)}/{total} files with issues)")
    else:
        report_results = results

    # Sort results: errors first, then warnings, then ok; then by path
    status_order = {"error": 0, "warning": 1, "ok": 2}
    report_results.sort(key=lambda r: (status_order[r.status], r.path))

    md = generate_markdown(report_results, root, elapsed)

    # Write with explicit UTF-8 and Unix line endings for cross-platform consistency
    output_path = Path(args.output)
    with output_path.open("w", encoding="utf-8", newline="\n") as f:
        f.write(md)
    print(f"\U0001f4c4 Report written to: {output_path.resolve()}")

    # Print a quick console summary
    ok_count    = sum(1 for r in results if r.status == "ok")
    warn_count  = sum(1 for r in results if r.status == "warning")
    error_count = sum(1 for r in results if r.status == "error")
    print(f"\n   \u2705 OK: {ok_count}  \u26a0\ufe0f  Warnings: {warn_count}  \u274c Errors: {error_count}")


if __name__ == "__main__":
    main()
