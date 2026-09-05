# Ulozi dily do .blend souboru k rucnimu prohlizeni a upravam.
#
# Skript je zdroj pravdy, .blend je odvozeny - kdyz v nem neco zmenis rucne,
# pri pristim prehnani build_pc_parts.py se to prepise. Na zkouseni to staci,
# na trvalou zmenu uprav skript.

import bpy, os, sys, math
SEM = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SEM)
from pc_parts_lib import cistit_scenu
import build_pc_parts as B

CIL_DILY = os.path.join(SEM, "Blender", "pc_dily.blend")
CIL_SESTAVA = os.path.join(SEM, "Blender", "pc_sestava.blend")
os.makedirs(os.path.dirname(CIL_DILY), exist_ok=True)

cistit_scenu()

mat = bpy.data.materials.new("Dil")
mat.use_nodes = True
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.30, 0.32, 0.36, 1)
b.inputs["Roughness"].default_value = 0.42
b.inputs["Metallic"].default_value = 0.25

SLOUPCU = 4
for i, (nazev, fn) in enumerate(B.STAVITELE):
    o = fn()
    o.name = nazev
    o.location = ((i % SLOUPCU - 1.5) * 0.15, -(i // SLOUPCU) * 0.175, 0)
    o.data.materials.append(mat)

# mrizka po 1 cm, aby sly rozmery odecist okem
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.length_unit = 'CENTIMETERS'

bpy.ops.wm.save_as_mainfile(filepath=CIL_DILY)
print("BLEND " + CIL_DILY)

# ---- druhy soubor: smontovana sestava ----

def obalka(o):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return (min(v.x for v in vs), max(v.x for v in vs),
            min(v.y for v in vs), max(v.y for v in vs),
            min(v.z for v in vs), max(v.z for v in vs))

cistit_scenu()

mat = bpy.data.materials.new("Dil")
mat.use_nodes = True
b2 = mat.node_tree.nodes["Principled BSDF"]
b2.inputs["Base Color"].default_value = (0.30, 0.32, 0.36, 1)
b2.inputs["Roughness"].default_value = 0.42
b2.inputs["Metallic"].default_value = 0.25

deska = B.deska(); deska.name = "Motherboard"
deska.location.z = -(obalka(deska)[4] + 0.0035)   # horni plocha PCB do z = 0
bpy.context.view_layer.update()

def montovat(nazev, fn, x, y, rot=(0, 0, 0), mezera=0.0):
    o = fn(); o.name = nazev
    o.rotation_euler = rot
    bpy.context.view_layer.update()
    x0, x1, y0, y1, z0, z1 = obalka(o)
    o.location = (x - (x0 + x1) / 2.0 + o.location.x,
                  y - (y0 + y1) / 2.0 + o.location.y,
                  mezera - z0 + o.location.z)
    bpy.context.view_layer.update()
    return o

dily = [deska]
cpu = montovat("Cpu", B.procesor, -0.018, 0.018); dily.append(cpu)
dily.append(montovat("Cooler", B.chladic, -0.018, 0.018, mezera=obalka(cpu)[5]))
for i, x in enumerate((0.026, 0.042)):
    dily.append(montovat("Ram%d" % i, B.pamet, x, 0.018,
                         rot=(math.radians(90), 0, math.radians(90))))
dily.append(montovat("Gpu", B.grafika, 0.004, -0.027, mezera=0.004))
dily.append(montovat("Ssd", B.disk, -0.006, -0.008))

psu = B.zdroj(); psu.name = "Psu"
bpy.context.view_layer.update()
x0, x1, y0, y1, z0, z1 = obalka(psu)
psu.location = (0.048 - (x0 + x1) / 2.0, -0.092 - (y0 + y1) / 2.0, -z0 - 0.006)
dily.append(psu)

for o in dily:
    o.data.materials.append(mat)

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.length_unit = 'CENTIMETERS'
bpy.ops.wm.save_as_mainfile(filepath=CIL_SESTAVA)
print("BLEND " + CIL_SESTAVA)
