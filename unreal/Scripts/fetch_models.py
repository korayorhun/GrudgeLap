"""Fetches the real car models the level dresses the catalogue cars with (Import/Models/models.json), from the
Objaverse mirror on Hugging Face: CC BY 4.0 models from Sketchfab, each with its LICENSE.txt beside it in the
repository (author, source, the credit line the licence asks for). Nothing is downloaded that is already there.
Usage: python fetch_models.py [Import/Models]
"""
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
    "golf-r32-mk4.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-060/8171332b39be45e896a7fdf3c3e66354.glb",
    "renault-12-boitaloran.glb": "https://huggingface.co/datasets/allenai/objaverse/resolve/main/glbs/000-074/f8c26a68a3dd45b492d6d9a33cda6fcd.glb",
}

folder = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Import", "Models")
os.makedirs(folder, exist_ok=True)
for name, url in MODELS.items():
    path = os.path.join(folder, name)
    if os.path.exists(path) and os.path.getsize(path) > 100000:
        print(f"{name}: present")
        continue
    print(f"{name}: downloading")
    with urllib.request.urlopen(url, timeout=120) as response, open(path, "wb") as out:
        while True:
            chunk = response.read(1 << 20)
            if not chunk:
                break
            out.write(chunk)
    print(f"{name}: {os.path.getsize(path) // 1024} KiB")
