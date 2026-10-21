# Packs the river's flow textures into Art/Resources/Environment/Water from R.A.M 2019 (River Auto Material,
# NatureManufacture), "River/Textures":
#   RiverFlowNormal.png  OpenGL normal map, tiling, 1024: flow-stretched ripples, streaks along V (EnvWater _FlowNormal)
#                        from T_water_02_N
#   RiverFoam.png        linear RGB, tiling, 1024 (EnvWater _FlowFoam): R bubbly foam (T_RAM_River_Foam(R)_Noise(A)),
#                        G the streaks' height (T_water_02_H, matching the normal map), B soft noise (T_RAM_River_Noise)
#
# usage: python -I pack_ram.py <R.A.M "River Auto Material/River/Textures" dir> <Art/Resources/Environment dir>
import hashlib
import os
import sys

from PIL import Image

src, out = sys.argv[1], os.path.join(sys.argv[2], "Water")
os.makedirs(out, exist_ok=True)
SIZE = 1024


# The same importer settings (and GUID scheme) as pack_water.py.
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


def load(name, mode):
    return Image.open(os.path.join(src, name)).convert(mode).resize((SIZE, SIZE), Image.LANCZOS)


load("T_water_02_N.png", "RGB").save(os.path.join(out, "RiverFlowNormal.png"))
foam = Image.merge("RGB", (load("T_RAM_River_Foam(R)_Noise(A).png", "L"), load("T_water_02_H.png", "L"), load("T_RAM_River_Noise.tga", "L")))
foam.save(os.path.join(out, "RiverFoam.png"))
for name, normal in (("RiverFlowNormal", True), ("RiverFoam", False)):
    with open(os.path.join(out, name + ".png.meta"), "w", newline="\n") as f:
        f.write(meta(name, normal, False))
print("packed", out)
