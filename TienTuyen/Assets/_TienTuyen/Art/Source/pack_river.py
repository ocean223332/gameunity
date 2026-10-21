# Packs the river's one texture into Art/Resources/Environment/River from R.A.M 2019 (River Auto Material,
# NatureManufacture), "River Auto Material/River/Textures":
#   RiverMap.png  linear RGBA, tiling, 1024 (EnvRiver _RiverMap): RG the ripple normal's x, y (T_RAM_River_Normalmap,
#                 OpenGL), B the crests' height (T_RAM_River_Height), A bubbly foam (T_RAM_River_Foam)
#
# usage: python -I pack_river.py <R.A.M "River Auto Material/River/Textures" dir> <Art/Resources/Environment dir>
import hashlib
import os
import sys

from PIL import Image

src, out = sys.argv[1], os.path.join(sys.argv[2], "River")
os.makedirs(out, exist_ok=True)
SIZE = 1024


def load(name, mode):
    return Image.open(os.path.join(src, name)).convert(mode).resize((SIZE, SIZE), Image.LANCZOS)


normal = load("T_RAM_River_Normalmap.png", "RGB").split()
Image.merge("RGBA", (normal[0], normal[1], load("T_RAM_River_Height.png", "L"), load("T_RAM_River_Foam.png", "L"))).save(
    os.path.join(out, "RiverMap.png"))
# High-quality compression (BC7 on desktop): the normal's two channels need it.
with open(os.path.join(out, "RiverMap.png.meta"), "w", newline="\n") as f:
    f.write(f"""fileFormatVersion: 2
guid: {hashlib.md5(b"TienTuyen/River/RiverMap").hexdigest()}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 1
    sRGBTexture: 0
  isReadable: 0
  maxTextureSize: 1024
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 8
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  alphaUsage: 1
  alphaIsTransparency: 0
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 1024
    textureFormat: -1
    textureCompression: 3
    compressionQuality: 50
  userData:
  assetBundleName:
  assetBundleVariant:
""")
print("packed", out)
