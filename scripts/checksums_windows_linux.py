#!/usr/bin/env python3
"""
music_checksum.py — Generate and verify SHA256 checksums for an entire music library.

Covers all file types found in a music library: FLAC, MP3, OPUS, OGG, lyrics (.lrc, .txt),
cover art (.jpg, .png), cue sheets (.cue), ripping logs (.log), and any other file present.
Skips system/junk files (DS_Store, Thumbs.db, desktop.ini, .tmp, .part, .lnk).

The manifest always uses forward slashes regardless of OS, making it cross-platform:
a manifest generated on Windows can be verified on Linux and vice versa.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
MODES
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  generate   Recursively scan the library and create a manifest of SHA256 checksums.
             Run this once to establish a baseline for your library.

  verify     Re-hash every file in the library and compare against the stored manifest.
             Use this periodically to detect corruption, bit rot, or accidental changes.
             Reports files that are: changed/corrupted, missing from disk, or unreadable.

  update     Sync the manifest after changes to the library (new albums added, files deleted).
             Only hashes files that are new — already known files are not re-hashed.
             Run this after every library update instead of regenerating from scratch.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
USAGE (Windows)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  # First-time setup: generate the manifest
  python music_checksum.py generate "D:\\Music"
  python music_checksum.py generate "D:\\Music" --output checksums.sha256
  python music_checksum.py generate "D:\\Music" --workers 4

  # Periodic integrity check: verify the full library against the manifest
  python music_checksum.py verify "D:\\Music" --manifest checksums.sha256
  python music_checksum.py verify "D:\\Music" --manifest checksums.sha256 --workers 4

  # After adding or removing files: update the manifest without full re-hash
  python music_checksum.py update "D:\\Music" --manifest checksums.sha256
  python music_checksum.py update "D:\\Music" --manifest checksums.sha256 --workers 4

  # Note: forward slashes also work on Windows and avoid escaping:
  python music_checksum.py generate "D:/Music"

  # Verify a single file using PowerShell (no Python needed):
  Get-FileHash -Algorithm SHA256 "D:\\Music\\Artist\\Album\\track.flac"

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
USAGE (Linux / Mac)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  python3 music_checksum.py generate /media/nas_music/
  python3 music_checksum.py verify /media/nas_music/ --manifest checksums.sha256
  python3 music_checksum.py update /media/nas_music/ --manifest checksums.sha256

  # Verify using the native Linux tool (from the library root):
  sha256sum -c checksums.sha256

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
OPTIONS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  --output / -o    (generate) Path for the manifest file. Default: checksums.sha256
  --manifest / -m  (verify, update) Path to an existing manifest file.
  --workers / -w   Number of parallel workers for hashing. Default: 4.
                   Since hashing is I/O-bound, you can safely go higher than your
                   CPU count when reading from a NAS or external drive.
"""

import argparse
import hashlib
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime
from pathlib import Path

# Force UTF-8 output on Windows so emojis and special characters display correctly.
# Windows terminals default to cp1252 which would break the progress bar and icons.
if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if sys.stderr.encoding and sys.stderr.encoding.lower() != "utf-8":
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")


# ---------------------------------------------------------------------------
# Hashing
# ---------------------------------------------------------------------------

CHUNK_SIZE = 1024 * 1024  # 1 MB read chunks — memory-friendly for large files


def sha256(path: Path) -> str:
    """Compute the SHA256 checksum of a file, reading in chunks."""
    h = hashlib.sha256()
    with path.open("rb") as f:
        while chunk := f.read(CHUNK_SIZE):
            h.update(chunk)
    return h.hexdigest()


def hash_file(path: Path, root: Path) -> tuple[Path, str | None, str]:
    """
    Hash a single file.
    Returns (path, hex_digest, error_message).
    hex_digest is None on failure.
    """
    try:
        digest = sha256(path)
        return (path, digest, "")
    except OSError as e:
        return (path, None, str(e))


# ---------------------------------------------------------------------------
# Path helpers
# ---------------------------------------------------------------------------

def to_manifest_key(path: Path, root: Path) -> str:
    """
    Return a forward-slash normalized relative path for use as a manifest key.
    Path.as_posix() always produces forward slashes, even on Windows,
    making the manifest cross-platform between Windows and Linux.
    """
    return path.relative_to(root).as_posix()


# ---------------------------------------------------------------------------
# File discovery
# ---------------------------------------------------------------------------

# Files to skip — system/temp/shortcut files that have no value in a music manifest
SKIP_SUFFIXES = {".tmp", ".part", ".crdownload", ".lnk"}
SKIP_NAMES    = {".DS_Store", "Thumbs.db", "desktop.ini"}

def find_all_files(root: Path) -> list[Path]:
    """Recursively find all files worth checksumming."""
    files = []
    for p in sorted(root.rglob("*")):
        if not p.is_file():
            continue
        if p.name in SKIP_NAMES:
            continue
        if p.suffix.lower() in SKIP_SUFFIXES:
            continue
        files.append(p)
    return files


# ---------------------------------------------------------------------------
# Manifest I/O
# ---------------------------------------------------------------------------

def write_manifest(entries: dict[str, str], output_path: Path) -> None:
    """
    Write a sha256sum-compatible manifest.
    Format: <hash>  <relative_path>
    Paths always use forward slashes for cross-platform compatibility.
    Entries are sorted by path for reproducibility.
    """
    with output_path.open("w", encoding="utf-8", newline="\n") as f:
        for rel_path in sorted(entries):
            digest = entries[rel_path]
            f.write(f"{digest}  {rel_path}\n")


def read_manifest(manifest_path: Path) -> dict[str, str]:
    """
    Parse a sha256sum-compatible manifest.
    Returns {relative_path: hex_digest}.
    Paths in the manifest are always forward-slash normalized.
    """
    entries = {}
    with manifest_path.open("r", encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n").rstrip("\r")
            if not line or line.startswith("#"):
                continue
            # Format: "<hash>  <path>" (two spaces)
            parts = line.split("  ", 1)
            if len(parts) != 2:
                continue
            digest, rel_path = parts
            # Normalize to forward slashes in case the manifest was edited on Windows
            rel_path = rel_path.replace("\\", "/")
            entries[rel_path] = digest.strip()
    return entries


# ---------------------------------------------------------------------------
# Progress
# ---------------------------------------------------------------------------

def print_progress(done: int, total: int, last_path: Path) -> None:
    pct = done / total * 100
    bar_len = 30
    filled = int(bar_len * done / total)
    bar = "█" * filled + "░" * (bar_len - filled)
    name = last_path.name[:38].ljust(38)
    print(f"\r[{bar}] {pct:5.1f}%  {done}/{total}  {name}", end="", flush=True)


# ---------------------------------------------------------------------------
# Generate
# ---------------------------------------------------------------------------

def cmd_generate(args: argparse.Namespace) -> None:
    root    = Path(args.root).resolve()
    output  = Path(args.output).resolve()
    workers = args.workers

    if not root.is_dir():
        print(f"ERROR: '{root}' is not a directory.", file=sys.stderr)
        sys.exit(1)

    if output.exists():
        answer = input(f"WARNING: '{output}' already exists. Overwrite? [y/N] ").strip().lower()
        if answer != "y":
            print("Aborted.")
            sys.exit(0)

    print(f"Scanning {root} ...")
    files = find_all_files(root)
    total = len(files)
    print(f"   Found {total} file(s). Hashing with {workers} worker(s)...\n")

    entries: dict[str, str] = {}
    errors:  list[tuple[Path, str]] = []
    start = datetime.now()

    with ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {executor.submit(hash_file, p, root): p for p in files}
        done = 0
        for future in as_completed(futures):
            path, digest, err = future.result()
            done += 1
            print_progress(done, total, path)
            rel = to_manifest_key(path, root)
            if digest:
                entries[rel] = digest
            else:
                errors.append((path, err))

    elapsed = (datetime.now() - start).total_seconds()
    print(f"\n\nDone in {elapsed:.1f}s.")

    write_manifest(entries, output)
    print(f"Manifest written to: {output}")
    print(f"\n   OK      : {len(entries)}")
    print(f"   Errors  : {len(errors)}")

    if errors:
        print("\nFiles that could not be hashed:\n")
        for path, err in errors:
            print(f"  {to_manifest_key(path, root)}")
            print(f"    -> {err}")

    print(f"\nTip: verify anytime with:")
    print(f"   python music_checksum.py verify \"{root}\" --manifest \"{output}\"")


# ---------------------------------------------------------------------------
# Verify
# ---------------------------------------------------------------------------

def cmd_verify(args: argparse.Namespace) -> None:
    root     = Path(args.root).resolve()
    manifest = Path(args.manifest).resolve()
    workers  = args.workers

    if not root.is_dir():
        print(f"ERROR: '{root}' is not a directory.", file=sys.stderr)
        sys.exit(1)
    if not manifest.exists():
        print(f"ERROR: Manifest not found: {manifest}", file=sys.stderr)
        sys.exit(1)

    print(f"Reading manifest: {manifest}")
    stored = read_manifest(manifest)
    total  = len(stored)
    print(f"   {total} entries found. Verifying with {workers} worker(s)...\n")

    ok_count      = 0
    changed:  list[str] = []
    missing:  list[str] = []
    errors:   list[tuple[str, str]] = []
    start = datetime.now()

    # Build list of (abs_path, rel_path) for files that exist on disk.
    # Manifest keys use forward slashes; Path() handles them correctly on Windows too.
    to_check: list[tuple[Path, str]] = []
    for rel_path in stored:
        abs_path = root / rel_path
        if abs_path.exists():
            to_check.append((abs_path, rel_path))
        else:
            missing.append(rel_path)

    with ThreadPoolExecutor(max_workers=workers) as executor:
        futures = {executor.submit(hash_file, abs_p, root): rel_p
                   for abs_p, rel_p in to_check}
        done = 0
        for future in as_completed(futures):
            rel_p = futures[future]
            abs_p, digest, err = future.result()
            done += 1
            print_progress(done + len(missing), total, abs_p)

            if err:
                errors.append((rel_p, err))
            elif digest != stored[rel_p]:
                changed.append(rel_p)
            else:
                ok_count += 1

    elapsed = (datetime.now() - start).total_seconds()
    print(f"\n\nVerification complete in {elapsed:.1f}s.\n")

    print(f"   OK               : {ok_count}")
    print(f"   Changed/corrupt  : {len(changed)}")
    print(f"   Missing          : {len(missing)}")
    print(f"   Read errors      : {len(errors)}")

    if changed:
        print("\nFiles whose checksum no longer matches (modified or corrupted):\n")
        for rel in sorted(changed):
            print(f"  {rel}")

    if missing:
        print("\nFiles in manifest but missing from disk:\n")
        for rel in sorted(missing):
            print(f"  {rel}")

    if errors:
        print("\nFiles that could not be read:\n")
        for rel, err in sorted(errors):
            print(f"  {rel}")
            print(f"    -> {err}")

    if not changed and not missing and not errors:
        print("\nEverything checks out — library is intact.")


# ---------------------------------------------------------------------------
# Update
# ---------------------------------------------------------------------------

def cmd_update(args: argparse.Namespace) -> None:
    root     = Path(args.root).resolve()
    manifest = Path(args.manifest).resolve()
    workers  = args.workers

    if not root.is_dir():
        print(f"ERROR: '{root}' is not a directory.", file=sys.stderr)
        sys.exit(1)
    if not manifest.exists():
        print(f"ERROR: Manifest not found: {manifest}", file=sys.stderr)
        sys.exit(1)

    print(f"Reading existing manifest: {manifest}")
    stored = read_manifest(manifest)
    print(f"   {len(stored)} entries in manifest.\n")

    print(f"Scanning {root} ...")
    disk_files = find_all_files(root)
    # Use forward-slash keys to match manifest format
    disk_rel   = {to_manifest_key(p, root): p for p in disk_files}
    print(f"   {len(disk_rel)} file(s) on disk.\n")

    # New files: on disk but not in manifest
    new_files     = {r: p for r, p in disk_rel.items() if r not in stored}
    # Deleted files: in manifest but not on disk
    deleted_files = {r for r in stored if r not in disk_rel}

    print(f"   New files     : {len(new_files)}")
    print(f"   Deleted files : {len(deleted_files)}")

    if not new_files and not deleted_files:
        print("\nManifest is already up to date. Nothing to do.")
        sys.exit(0)

    updated = dict(stored)

    if new_files:
        print(f"\nHashing {len(new_files)} new file(s) with {workers} worker(s)...\n")
        errors: list[tuple[str, str]] = []
        start = datetime.now()

        with ThreadPoolExecutor(max_workers=workers) as executor:
            futures = {executor.submit(hash_file, p, root): r
                       for r, p in new_files.items()}
            done = 0
            for future in as_completed(futures):
                rel_p = futures[future]
                abs_p, digest, err = future.result()
                done += 1
                print_progress(done, len(new_files), abs_p)
                if digest:
                    updated[rel_p] = digest
                else:
                    errors.append((rel_p, err))

        elapsed = (datetime.now() - start).total_seconds()
        print(f"\n   Done in {elapsed:.1f}s.")

        if errors:
            print("\nFiles that could not be hashed:\n")
            for rel, err in errors:
                print(f"  {rel}\n    -> {err}")

    for rel in deleted_files:
        del updated[rel]

    write_manifest(updated, manifest)
    print(f"\nManifest updated: {manifest}")
    print(f"   Added   : {len(new_files)}")
    print(f"   Removed : {len(deleted_files)}")
    print(f"   Total   : {len(updated)}")


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate and verify SHA256 checksums for a music library.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    sub = parser.add_subparsers(dest="command", required=True)

    # --- generate ---
    gen = sub.add_parser("generate", help="Scan and create a checksum manifest")
    gen.add_argument("root", help="Root directory of the music library")
    gen.add_argument("--output", "-o", default="checksums.sha256",
                     help="Output manifest file (default: checksums.sha256)")
    gen.add_argument("--workers", "-w", type=int, default=4,
                     help="Number of parallel workers (default: 4)")

    # --- verify ---
    ver = sub.add_parser("verify", help="Verify the library against an existing manifest")
    ver.add_argument("root", help="Root directory of the music library")
    ver.add_argument("--manifest", "-m", required=True, help="Path to the manifest file")
    ver.add_argument("--workers", "-w", type=int, default=4,
                     help="Number of parallel workers (default: 4)")

    # --- update ---
    upd = sub.add_parser("update", help="Add new files / remove deleted files from manifest")
    upd.add_argument("root", help="Root directory of the music library")
    upd.add_argument("--manifest", "-m", required=True, help="Path to the manifest file")
    upd.add_argument("--workers", "-w", type=int, default=4,
                     help="Number of parallel workers (default: 4)")

    args = parser.parse_args()

    if args.command == "generate":
        cmd_generate(args)
    elif args.command == "verify":
        cmd_verify(args)
    elif args.command == "update":
        cmd_update(args)


if __name__ == "__main__":
    main()
