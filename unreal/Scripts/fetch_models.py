"""Fetches the real car models the level dresses the catalogue cars with (Import/Models/models.json), from the
Objaverse mirror on Hugging Face: CC BY 4.0 models from Sketchfab, each with its LICENSE.txt beside it in the
repository (author, source, the credit line the licence asks for). A model uploaded after the mirror's 2022 snapshot
(RELEASED) comes from the project's own copy, a release asset of the repository, and is checked against the SHA-256 of
the file its LICENSE.txt describes. Nothing is downloaded that is already there.
Usage: python fetch_models.py [Import/Models]
"""
import hashlib
import os
import sys
import urllib.request

MODELS = {
    "lotus-esprit-turbo-1983.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-040/886bcf8ce52f46e3b36115e01319ec44.glb",
    "maserati-granturismo-mc.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-009/c4aa1cf6461048e3b75a77ff027f90c6.glb",
    "civic-type-r-ek9.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-066/b9604dd71c6546ec842702682019492a.glb",
    "saab-9-5-sedan.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-127/4db0bcbc183a452eb3ef692cce2fe36f.glb",
    "mercedes-e-w212.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-132/119c5e10733142b197aa53b86f6aeb04.glb",
    # The game's (tools/car_models.json, 0.16): the V8, the hatchbacks and the 1990s saloon, each with its cabin.
    "mclaren-570s-mmcworks.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-132/869feee8011d4803894318f6f0ddf2c6.glb",
    "peugeot-205-gti-lafreizz.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-079/b52c8e5e8070463ab1485039246f527c.glb",
    "vaz-2106-domhathair.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-047/a73b3cdeddd846209bf7c73d5f04d20f.glb",
    # 0.17: the flat-twelve as the Testarossa it always was (the Esprit lends it its cabin), and three different hatches.
    "testarossa-liosh.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-141/762c958bd80f4cac8e4dc76f847fca49.glb",
    "lancia-delta-integrale-tarantula.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-140/85614131e0dc4613a948472aaa935fc7.glb",
    # The herd's cows (0.51, game/src/CowBuilder.cs): a Holstein on a quadruped's skeleton.
    "cow-holstein-nyilonelycompany.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-099/c5782e43dcf442718a25b49bed00f582.glb",
    "golf-r32-mk4.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-060/8171332b39be45e896a7fdf3c3e66354.glb",
    "renault-12-boitaloran.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-074/f8c26a68a3dd45b492d6d9a33cda6fcd.glb",
    # models13: the flat-plane V8 as a Ferrari 458 Italia (Antonis_zks; a shell without a cabin, like the Testarossa, so
    # the game lends it the 570S's). No CC BY model of the first-generation Fiat Bravo or of the 2018 SEAT Leon Cupra
    # exists (a search of the whole Objaverse catalogue, Sketchfab's CC BY/CC0 listings, archive.org, Commons and
    # OpenGameArt on 2026-11-02 found none that is not a game rip or behind a login): those two keep lofted bodies of
    # their own (game/src/CarView.Cars13.cs).
    "ferrari-458-italia-antonis.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-011/9fc3bebac5ee4ad1a7fb1d743e0db7d3.glb",
}

# Models the mirror does not hold: the project's copy of the file the LICENSE.txt describes, its SHA-256, and its
# Sketchfab uid (the LICENSE.txt and THIRD_PARTY.md name the page by it).
RELEASED = {
    # cars13 bodies: the BRZ as Ethan's first-generation Toyota 86 (ZN6), the BRZ ZC6's twin (uploaded 2023-06-25).
    "toyota-86-ethanchew.glb": {
        "url": "https://github.com/korayorhun/GrudgeLap/releases/download/models-2026-11/toyota-86-ethanchew.glb",
        "sha256": "d7ff3342739b1bdc322615a472c7c3e1da1bb47bf76b93f4125f218100649efe",
        "sketchfab": "5b8ec05b53b345cda8ed582b59ff3950",
    },
    # A1 (the owner's Clio, 0.33.0): Nardeol's Renault Clio II phase 2 five-door, a shell without a cabin (published
    # 2023-05-11), the only credible CC BY Clio of the 115 Sketchfab models checked on 2026-11-03 (its LICENSE.txt).
    "renault-clio-2-nardeol.glb": {
        "url": "https://github.com/korayorhun/GrudgeLap/releases/download/models-2026-11/renault-clio-2-nardeol.glb",
        "sha256": "5b10089bf086db5354d7e8165ba04a73135d234dc4c0bf1e29eec7dbc5f1494c",
        "sketchfab": "2c4d2cb0cda14dc9b2ddcd7d2b3a1f95",
    },
}


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Import", "Models")
os.makedirs(folder, exist_ok=True)
sources = {**MODELS, **{name: entry["url"] for name, entry in RELEASED.items()}}
for name, url in sources.items():
    path = os.path.join(folder, name)
    expected = RELEASED.get(name, {}).get("sha256")
    if os.path.exists(path) and os.path.getsize(path) > 100000 and (expected is None or sha256(path) == expected):
        print(f"{name}: present")
        continue
    print(f"{name}: downloading")
    part = path + ".part"
    with urllib.request.urlopen(url, timeout=120) as response, open(part, "wb") as out:
        while True:
            chunk = response.read(1 << 20)
            if not chunk:
                break
            out.write(chunk)
    if expected is not None and sha256(part) != expected:
        os.remove(part)
        sys.exit(f"{name}: the download is not the file its LICENSE.txt describes (SHA-256 {expected})")
    os.replace(part, path)
    print(f"{name}: {os.path.getsize(path) // 1024} KiB")
