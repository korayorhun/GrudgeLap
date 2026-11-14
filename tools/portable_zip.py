"""The portable zip of a release, and the car models' credits that go with every copy (0.78).

    python tools/portable_zip.py build/unreal dist/GrudgeLap-Portable-<version>.zip
    python tools/portable_zip.py --credits build/unreal/CREDITS-car-models.txt

The zip holds the packaged game (tools/package.ps1's build/unreal) with its files at the zip's root, GrudgeLap.exe
first among them: a portable copy's updater unpacks it over the game's folder and starts that launcher again. Until
0.77 the portable zip was the Godot game's and this script, as unreal/Scripts/preview_zip.py, packed the Unreal
version's preview beside it.
"""
import os
import sys
import zipfile

REPOSITORY = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def credits():
    """The credits of the car models the game draws: each a row of THIRD_PARTY.md with its author, source and changes,
    which the CC BY licence asks for with every copy."""
    rows = [line for line in open(os.path.join(REPOSITORY, "THIRD_PARTY.md"), encoding="utf-8").read().splitlines()
            if line.startswith("| ") and "sketchfab.com/3d-models" in line]
    notes = []
    for row in rows:
        cells = [c.strip() for c in row.strip("|").split("|")]
        if len(cells) >= 3:
            notes.append(f"{cells[0]}\n  used for: {cells[1]}\n  licence: {cells[2]}")
    return ("The car models GrudgeLap draws (CC BY 4.0 unless a row says otherwise; the makes are the authors' "
            "subjects). THIRD_PARTY.md beside this file lists everything else.\n\n" + "\n\n".join(notes) + "\n")


def pack(source, target):
    if not os.path.exists(os.path.join(source, "GrudgeLap.exe")):
        sys.exit(f"{source} has no GrudgeLap.exe: not the packaged game (tools/package.ps1)")
    os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
    count = 0
    # Level 6: the cooked content is compressed already and the executable is not; 1 left the zip 15 % larger.
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for folder, dirs, names in os.walk(source):
            dirs[:] = sorted(d for d in dirs if d != "Saved")
            relative = os.path.relpath(folder, source)
            for name in sorted(names):
                if name.endswith(".pdb") or name.startswith("Manifest_"):
                    continue
                archive.write(os.path.join(folder, name), os.path.normpath(os.path.join(relative, name)))
                count += 1
    print(f"{target}: {count} files, {os.path.getsize(target) / 1e9:.2f} GB")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--credits":
        with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as out:
            out.write(credits())
    elif len(sys.argv) == 3:
        pack(sys.argv[1], sys.argv[2])
    else:
        sys.exit(__doc__)
