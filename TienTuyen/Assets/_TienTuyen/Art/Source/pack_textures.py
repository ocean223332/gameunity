# Packs the Poly Haven texture sets into Art/Resources/Environment for the environment shaders:
#   <Set>_Albedo.png  RGB albedo, A height (displacement)
#   <Set>_Normal.jpg  OpenGL normal map
#   <Set>_Mask.jpg    R ambient occlusion, G smoothness
# Ground/ holds the four EnvGround.shader layers (2048, mask 1024), Surface/ the EnvSurface.shader sets (1024).
# usage: python -I pack_textures.py <downloads/polyhaven dir> <Art/Resources/Environment dir>
import os
import sys

from PIL import Image, ImageOps

src, out = sys.argv[1], sys.argv[2]
SETS = {
    "Ground": (2048, 1024, {
        "Laterite": ("red_laterite_soil_stones", "diff"),
        "Mud": ("brown_mud_02", "diff"),
        "Litter": ("forest_leaves_02", "diffuse"),
        "Grass": ("leafy_grass", "diff"),
    }),
    "Surface": (1024, 1024, {
        "Rock": ("rock_boulder_dry", "diff"),
        "Bark": ("bark_brown_02", "diff"),
    }),
}
for folder_name, (size, mask_size, layers) in SETS.items():
    folder_out = os.path.join(out, folder_name)
    os.makedirs(folder_out, exist_ok=True)
    for layer, (asset, diffuse) in layers.items():
        folder = os.path.join(src, asset)
        path = lambda kind: os.path.join(folder, f"{asset}_{kind}_2k.jpg")
        albedo = Image.open(path(diffuse)).convert("RGB").resize((size, size), Image.LANCZOS)
        height = Image.open(path("disp")).convert("L").resize(albedo.size, Image.LANCZOS)
        height = ImageOps.autocontrast(height, cutoff=1)
        rgba = albedo.copy()
        rgba.putalpha(height)
        rgba.save(os.path.join(folder_out, f"{layer}_Albedo.png"), optimize=True)
        normal = Image.open(path("nor_gl")).convert("RGB").resize((size, size), Image.LANCZOS)
        normal.save(os.path.join(folder_out, f"{layer}_Normal.jpg"), quality=95)
        ao = Image.open(path("ao")).convert("L").resize((mask_size, mask_size), Image.LANCZOS)
        smooth = ImageOps.invert(Image.open(path("rough")).convert("L").resize((mask_size, mask_size), Image.LANCZOS))
        Image.merge("RGB", (ao, smooth, Image.new("L", (mask_size, mask_size), 0))).save(os.path.join(folder_out, f"{layer}_Mask.jpg"), quality=95)
        print(folder_name, layer, asset, albedo.size)
