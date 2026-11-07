"""Packs a packaged build of the Unreal version into the release's preview archive (0.44).

    python unreal/Scripts/preview_zip.py <archive folder>/Windows dist/GrudgeLapUE-Preview-<version>.zip

The archive is BuildCookRun's -archivedirectory output (README, Packaging) without its debug symbols (the .pdb, 2.6 GB:
GitHub takes 2 GB a file) and without a Saved folder (a test run's logs, screenshots and crash reports), with the
repository's THIRD_PARTY.md and the credits of the car models the level draws (the CC BY licence asks for them with
every copy) beside the executable.
"""
import json
import os
import sys
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
UNREAL = os.path.dirname(HERE)
REPOSITORY = os.path.dirname(UNREAL)


def credits():
    """The licence notes of the models models.json puts in the level, one after another."""
    models = os.path.join(UNREAL, "Import", "Models")
    catalogue = json.load(open(os.path.join(models, "models.json"), encoding="utf-8"))
    files = sorted({entry["file"] for key, entry in catalogue.items() if isinstance(entry, dict) and "file" in entry})
    notes = []
    for name in files:
        stem = os.path.splitext(name)[0]
        path = os.path.join(models, stem + ".LICENSE.txt")
        if os.path.exists(path):
            notes.append(open(path, encoding="utf-8").read().strip())
    return ("The car models drawn by the Unreal Engine version (CC BY 4.0; the makes are the authors' subjects):\n\n"
            + "\n\n".join(notes) + "\n")


def main(source, target):
    os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
    count = 0
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for folder, dirs, names in os.walk(source):
            relative = os.path.relpath(folder, source)
            dirs[:] = [d for d in dirs if d != "Saved"]
            for name in names:
                if name.endswith(".pdb") or name.startswith("Manifest_DebugFiles"):
                    continue
                path = os.path.join(folder, name)
                archive.write(path, os.path.normpath(os.path.join("GrudgeLapUE", relative, name)))
                count += 1
        archive.write(os.path.join(REPOSITORY, "THIRD_PARTY.md"), "GrudgeLapUE/THIRD_PARTY.md")
        archive.writestr("GrudgeLapUE/CREDITS-car-models.txt", credits())
    print(f"{target}: {count} files, {os.path.getsize(target) / 1e9:.2f} GB")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
