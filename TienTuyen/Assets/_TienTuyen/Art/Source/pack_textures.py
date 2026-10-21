# Packs texture sets into Art/Resources/Environment for the environment shaders.
#
# Ground/ holds the EnvGround.shader layers as Unity TerrainLayer assets (<Layer>.terrainlayer), so a layer can be
# swapped for any TerrainLayer without code changes. Each layer:
#   <Layer>_Diffuse.png  RGB albedo (2048; Grass2 512)
#   <Layer>_Normal.jpg   OpenGL normal map
#   <Layer>_Mask.png     HDRP/URP-terrain mask at half size: R metallic, G ambient occlusion, B height, A smoothness
# Tile size (m) is written once here and tuned in the asset. The Ground/ modes pack Asset Store tile sets:
#   Meadow: Rocky Hills Environment Mega Pack (Toby Fredson) grass: Grass (GrassTileRHE) and Grass2
#           (GrassLightSamllTileRHE, a lighter, flowering sward mixed in patches).
#   Rocky:  the same pack's soil, mud, pine-needle and rock tiles.
#   Moss:   Mountain Lake Forest Pack (VIVID Arts) moss, mixed in patches into the needles.
# An albedo may get a per-channel gain to the linear average the shaders were tuned for.
#
# Surface/ holds the EnvSurface.shader sets from Poly Haven (1024):
#   <Set>_Albedo.png  RGB albedo, A height (displacement)
#   <Set>_Normal.jpg  OpenGL normal map
#   <Set>_Mask.jpg    R ambient occlusion, G smoothness
#
# usage: python -I pack_textures.py <downloads/polyhaven dir> <Art/Resources/Environment dir> [Surface]
#        python -I pack_textures.py <Rocky Hills "Terrain Tiles_Textures" dir> <Art/Resources/Environment dir> Meadow
#        python -I pack_textures.py <Rocky Hills tile textures dir> <Art/Resources/Environment dir> Rocky
#        python -I pack_textures.py <Mountain Lake "MountainLake/Textures" dir> <Art/Resources/Environment dir> Moss
import hashlib
import os
import sys

from PIL import Image, ImageOps

src, out = sys.argv[1], sys.argv[2]
only = sys.argv[3] if len(sys.argv) > 3 else None

# mode: layer: (albedo, normal, height, mask with G AO and A smoothness, texture size, tile size m,
# linear average after the gain or None for none)
TILE_SETS = {
    "Meadow": {
        "Grass": ("GrassTileRHE_Albedo", "GrassTileRHE_Normal", "GrassTileRHE_Hight", "GrassTileRHE_MetalnessAoGloss",
                  2048, 3.5, (0.207, 0.237, 0.069)),
        "Grass2": ("GrassLightSamllTileRHE_Albedo", "GrassLightSamllTileRHE_Normal", "GrassLightSamllTileRHE_Hight",
                   "GrassLightSamllTileRHE_MetalnessAoGloss", 512, 1.2, (0.225, 0.26, 0.062)),
    },
    # The rest of the ground from the same pack, each gained to the colour the shaders were tuned for.
    "Rocky": {
        "Soil": ("DirtMainTileRHE_Albedo", "DirtMainTileRHE_Normal", "DirtMainTileRHE_Hight", "DirtMainTileRHE_MetalnessAoGloss",
                 2048, 3.0, (0.1305, 0.048, 0.025)),
        "Mud": ("MudATileRHE_Albedo", "MudATileRHE_Normal", "MudATileRHE_Hight", "MudATileRHE_MetalnessAoGloss",
                2048, 3.0, (0.0768, 0.0604, 0.0398)),
        "Needles": ("GroundStiks_Terrain_Albedo", "GroundStiks_Terrain_Normal", "GroundStiks_Terrain_Height", "GroundStiks_MetalnessAoGloss",
                    2048, 2.5, (0.16, 0.095, 0.045)),
        "Rock": ("RockBaseTileRHE_Albedo", "RockBaseTileRHE_Normal", "RockBaseTileRHE_Hight", "RockBaseTileRHE_MetalnessAoGloss",
                 2048, 4.0, (0.14, 0.13, 0.115)),
    },
    "Moss": {
        "Moss": ("ML_Moss_Green_Ground_BaseColor", "ML_Moss_Green_Ground_Normal", "ML_Moss_Green_Ground_Height",
                 "ML_Moss_Green_Ground_MaskMap", 2048, 2.5, None),
    },
}
SURFACE = {
    "Rock": "rock_boulder_dry",
    "Bark": "bark_brown_02",
}


def path(asset, kind):
    return os.path.join(src, asset, f"{asset}_{kind}_2k.jpg")


def guid(name):
    return hashlib.md5(("TienTuyen/Ground/" + name).encode()).hexdigest()


def texture_meta(name, normal=False, srgb=True):
    return f"""fileFormatVersion: 2
guid: {guid(name)}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 1
    sRGBTexture: {1 if srgb else 0}
  isReadable: 0
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 8
    mipBias: 0
    wrapU: 0
    wrapV: 0
    wrapW: 0
  nPOTScale: 1
  alphaUsage: {0 if normal else 1}
  alphaIsTransparency: 0
  textureType: {1 if normal else 0}
  textureShape: 1
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


def terrain_layer(layer, tile, tint):
    ref = lambda kind: f"{{fileID: 2800000, guid: {guid(layer + '_' + kind)}, type: 3}}"
    return f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1953259897 &8574412962073106934
TerrainLayer:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_Name: {layer}
  m_DiffuseTexture: {ref('Diffuse')}
  m_NormalMapTexture: {ref('Normal')}
  m_MaskMapTexture: {ref('Mask')}
  m_TileSize: {{x: {tile}, y: {tile}}}
  m_TileOffset: {{x: 0, y: 0}}
  m_Specular: {{r: 0, g: 0, b: 0, a: 0}}
  m_Metallic: 0
  m_Smoothness: 0
  m_NormalScale: 1
  m_DiffuseRemapMin: {{x: 0, y: 0, z: 0, w: 0}}
  m_DiffuseRemapMax: {{x: {tint[0]}, y: {tint[1]}, z: {tint[2]}, w: 1}}
  m_MaskMapRemapMin: {{x: 0, y: 0, z: 0, w: 0}}
  m_MaskMapRemapMax: {{x: 1, y: 1, z: 1, w: 1}}
"""


def layer_meta(layer):
    return f"""fileFormatVersion: 2
guid: {guid(layer + '_TerrainLayer')}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 8574412962073106934
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def write(file, text):
    with open(file, "w", encoding="utf8", newline="\n") as f:
        f.write(text)


ground = os.path.join(out, "Ground")
os.makedirs(ground, exist_ok=True)


def srgb_to_linear(c):
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


def linear_gain(image, gain):
    """Per-channel gain in linear light on an 8-bit sRGB image."""
    lut = []
    for g in gain:
        for v in range(256):
            x = srgb_to_linear([v / 255])[0] * g
            x = min(x, 1.0)
            y = x * 12.92 if x <= 0.0031308 else 1.055 * x ** (1 / 2.4) - 0.055
            lut.append(round(y * 255))
    return image.point(lut)


def linear_average(image):
    small = image.resize((64, 64), Image.BOX)
    pixels = list(small.getdata())
    return [sum(srgb_to_linear([p[i] / 255])[0] for p in pixels) / len(pixels) for i in range(3)]


for layer, (albedo_name, normal_name, height_name, mask_name, size, tile, average) in TILE_SETS.get(only, {}).items():
    load = lambda name: Image.open(os.path.join(src, name + ".png"))
    albedo = load(albedo_name).convert("RGB")
    gain = [t / a for t, a in zip(average, linear_average(albedo))] if average else (1, 1, 1)
    linear_gain(albedo, gain).resize((size, size), Image.LANCZOS).save(os.path.join(ground, f"{layer}_Diffuse.png"), optimize=True)
    load(normal_name).convert("RGB").resize((size, size), Image.LANCZOS).save(os.path.join(ground, f"{layer}_Normal.jpg"), quality=95)
    mask_size = (size // 2, size // 2)
    height = ImageOps.autocontrast(load(height_name).convert("L").resize(mask_size, Image.LANCZOS), cutoff=1)
    _, ao, _, gloss = load(mask_name).convert("RGBA").resize(mask_size, Image.LANCZOS).split()
    Image.merge("RGBA", (Image.new("L", mask_size, 0), ao, height, gloss)).save(os.path.join(ground, f"{layer}_Mask.png"), optimize=True)
    write(os.path.join(ground, f"{layer}_Diffuse.png.meta"), texture_meta(layer + "_Diffuse"))
    write(os.path.join(ground, f"{layer}_Normal.jpg.meta"), texture_meta(layer + "_Normal", normal=True, srgb=False))
    write(os.path.join(ground, f"{layer}_Mask.png.meta"), texture_meta(layer + "_Mask", srgb=False))
    write(os.path.join(ground, f"{layer}.terrainlayer"), terrain_layer(layer, tile, (1.0, 1.0, 1.0)))
    write(os.path.join(ground, f"{layer}.terrainlayer.meta"), layer_meta(layer))
    print(only, layer, albedo_name, "gain", [round(g, 3) for g in gain])

surface = os.path.join(out, "Surface")
os.makedirs(surface, exist_ok=True)
for layer, asset in (SURFACE.items() if only in (None, "Surface") else ()):
    albedo = Image.open(path(asset, "diff")).convert("RGB").resize((1024, 1024), Image.LANCZOS)
    height = ImageOps.autocontrast(Image.open(path(asset, "disp")).convert("L").resize(albedo.size, Image.LANCZOS), cutoff=1)
    rgba = albedo.copy()
    rgba.putalpha(height)
    rgba.save(os.path.join(surface, f"{layer}_Albedo.png"), optimize=True)
    Image.open(path(asset, "nor_gl")).convert("RGB").resize(albedo.size, Image.LANCZOS).save(os.path.join(surface, f"{layer}_Normal.jpg"), quality=95)
    ao = Image.open(path(asset, "ao")).convert("L").resize(albedo.size, Image.LANCZOS)
    smooth = ImageOps.invert(Image.open(path(asset, "rough")).convert("L").resize(albedo.size, Image.LANCZOS))
    Image.merge("RGB", (ao, smooth, Image.new("L", albedo.size, 0))).save(os.path.join(surface, f"{layer}_Mask.jpg"), quality=95)
    print("Surface", layer, asset)
