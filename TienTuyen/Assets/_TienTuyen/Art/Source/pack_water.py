# Packs the river-valley water textures into Art/Resources/Environment/Water from two Asset Store packs:
#   WaterfallFoam.png  greyscale white water, tiling (EnvWaterfall _FoamTex): Rocky Hills Environment Mega Pack
#                      (Toby Fredson) WaterfallFoamRHE_Albedo
#   MistPuff.png       greyscale cloud on a black border, clamped (EnvMist _PuffTex): the same pack's CloudsA
#   RiverRipples.png   OpenGL normal map, tiling, 1024 (EnvWater _RippleNormal): Mountain Lake Forest Pack
#                      (VIVID Arts) Ocean_Shallow_Normal
#
# usage: python -I pack_water.py <Rocky Hills "Textures/Particle_Textures" dir> <Mountain Lake "Shared/Water" dir>
#                                <Art/Resources/Environment dir>
import hashlib
import os
import sys

from PIL import Image

particles, water, out = sys.argv[1], sys.argv[2], os.path.join(sys.argv[3], "Water")
os.makedirs(out, exist_ok=True)

# name: (source, size, normal map, clamped)
SETS = {
    "WaterfallFoam": (os.path.join(particles, "WaterfallFoamRHE_Albedo.tif"), 512, False, False),
    "MistPuff": (os.path.join(particles, "CloudsA.png"), 512, False, True),
    "RiverRipples": (os.path.join(water, "Ocean_Shallow_Normal.png"), 1024, True, False),
}


def meta(name, normal, clamped):
    wrap = 1 if clamped else 0
    return f"""fileFormatVersion: 2
guid: {hashlib.md5(("TienTuyen/Water/" + name).encode()).hexdigest()}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 1
    sRGBTexture: 0
  isReadable: 0
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 4
    mipBias: 0
    wrapU: {wrap}
    wrapV: {wrap}
    wrapW: {wrap}
  nPOTScale: 1
  alphaUsage: 0
  alphaIsTransparency: 0
  textureType: {1 if normal else 0}
  textureShape: 1
  singleChannelComponent: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
  userData:
  assetBundleName:
  assetBundleVariant:
"""


for name, (source, size, normal, clamped) in SETS.items():
    image = Image.open(source)
    image = image.convert("RGB") if normal else image.convert("L")
    if image.size != (size, size):
        image = image.resize((size, size), Image.LANCZOS)
    file = os.path.join(out, name + ".png")
    image.save(file, optimize=True)
    with open(file + ".meta", "w", encoding="utf8", newline="\n") as f:
        f.write(meta(name, normal, clamped))
    print(name, image.mode, image.size, os.path.getsize(file))
