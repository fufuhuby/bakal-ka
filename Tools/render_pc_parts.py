# Nahledovy render vsech dilu do mrizky se jmenovkami.
# Kontroluje se SILUETA: dil musi byt poznat i jednobarevny, protoze
# v uloze mu barvu urcuje paleta a ta se mezi pokusy meni.

import bpy, os, sys, math
SEM = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SEM)
from pc_parts_lib import cistit_scenu
import build_pc_parts as B

KOREN = os.path.dirname(SEM)
CIL = os.path.join(KOREN, "Assets", "Screenshots", "pc_dily_nahled.png")

cistit_scenu()

SLOUPCU = 4
KROK_X, KROK_Y = 0.150, 0.175

mat = bpy.data.materials.new("Dil")
mat.use_nodes = True
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.30, 0.32, 0.36, 1)
b.inputs["Roughness"].default_value = 0.42
b.inputs["Metallic"].default_value = 0.25

matText = bpy.data.materials.new("Text")
matText.use_nodes = True
tb = matText.node_tree.nodes["Principled BSDF"]
tb.inputs["Base Color"].default_value = (0.85, 0.87, 0.92, 1)
tb.inputs["Roughness"].default_value = 1.0

for i, (nazev, fn) in enumerate(B.STAVITELE):
    sl, rd = i % SLOUPCU, i // SLOUPCU
    x = (sl - (SLOUPCU - 1) / 2.0) * KROK_X
    y = -rd * KROK_Y

    o = fn()
    o.name = nazev
    # Puvod je ve stredu obalky (kvuli validatoru v Unity), takze by se
    # spodni polovina dilu propadla pod podlozku. Pro nahled se dil nadzvedne.
    o.location = (x, y, 0)
    o.data.materials.append(mat)

    bpy.ops.object.text_add(location=(x, y - 0.080, 0.0))
    t = bpy.context.object
    t.data.body = nazev
    t.data.size = 0.011
    t.data.align_x = 'CENTER'
    t.data.extrude = 0.0004
    t.data.materials.append(matText)

# Zadna podlozka: plocha deska na ni splyva a vypada, ze chybi.
# Tmave pozadi zaroven ukaze presne to, co se kontroluje - siluetu.
sv = bpy.data.worlds.new("Pozadi")
sv.use_nodes = True
sv.node_tree.nodes["Background"].inputs["Color"].default_value = (0.045, 0.05, 0.065, 1)
sv.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
bpy.context.scene.world = sv

# Kamera shora zeshora-zepredu: horni plocha i silueta z boku najednou
stred_y = -((len(B.STAVITELE) - 1) // SLOUPCU) * KROK_Y / 2.0
bpy.ops.object.camera_add(location=(0.10, stred_y - 0.38, 0.26),
                          rotation=(math.radians(58), 0, math.radians(14)))
kam = bpy.context.object
kam.data.type = 'ORTHO'
kam.data.ortho_scale = 0.66
bpy.context.scene.camera = kam

bpy.ops.object.light_add(type='AREA', location=(0.30, stred_y - 0.30, 0.40))
bpy.context.object.data.energy = 22
bpy.context.object.data.size = 0.5
bpy.ops.object.light_add(type='AREA', location=(-0.40, stred_y - 0.25, 0.20))
bpy.context.object.data.energy = 9
bpy.context.object.data.size = 0.8
bpy.ops.object.light_add(type='AREA', location=(0.0, stred_y + 0.35, 0.30))
bpy.context.object.data.energy = 7
bpy.context.object.data.size = 1.0

s = bpy.context.scene
dostupne = [p.identifier for p in s.render.bl_rna.properties['engine'].enum_items]
for jmeno in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'CYCLES'):
    if jmeno in dostupne:
        s.render.engine = jmeno
        break

s.render.resolution_x = 1500
s.render.resolution_y = 860
s.render.filepath = CIL
bpy.ops.render.render(write_still=True)
print("NAHLED " + CIL)
