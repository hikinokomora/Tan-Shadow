#!/usr/bin/env python3
# Megascans «Rock Cliff» (wgoradh, 2K, 2×2 м) → текстуры HDRP Lit для камней насыпи осени.
# Запуск из корня репозитория: python3 ArtSource/Environment/Autumn/RockCliff/make_rock_cliff.py  (Pillow, numpy)
# Нормали скана — OpenGL (Y+), как у Unity: копируются без переворота.
# Маска HDRP: R металл 0, G AO, B маска деталей 0, A гладкость = 1 − Roughness.
import os
import shutil
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Environment", "Autumn", "Location", "Textures")
SRC = "Rock_Cliff_wgoradh_2K_{}.jpg"

shutil.copyfile(os.path.join(HERE, SRC.format("BaseColor")), os.path.join(OUT, "T_RockCliff_BaseColor.jpg"))
shutil.copyfile(os.path.join(HERE, SRC.format("Normal")), os.path.join(OUT, "T_RockCliff_Normal.jpg"))

ao = np.asarray(Image.open(os.path.join(HERE, SRC.format("AO"))).convert("L"))
roughness = np.asarray(Image.open(os.path.join(HERE, SRC.format("Roughness"))).convert("L"))
mask = np.zeros(ao.shape + (4,), dtype=np.uint8)
mask[..., 1] = ao
mask[..., 3] = 255 - roughness
Image.fromarray(mask, "RGBA").save(os.path.join(OUT, "T_RockCliff_Mask.png"))
print("Rock Cliff textures written to", OUT)
