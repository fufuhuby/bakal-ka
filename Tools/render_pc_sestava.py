# Nahled hotove sestavy - to je predloha, kterou bude participant replikovat.
# Deska je zaroven montazni rovina: v Unity stoji svisle a vsechny dily
# se montuji na jeji celo, takze se navzajem nezakryvaji.

import bpy, os, sys, math
SEM = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SEM)
from pc_parts_lib import cistit_scenu
import build_pc_parts as B

KOREN = os.path.dirname(SEM)
CIL = os.path.join(KOREN, "Assets", "Screenshots", "pc_sestava_nahled.png")

cistit_scenu()

def obalka(o):
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    return (min(v.x for v in vs), max(v.x for v in vs),
            min(v.y for v in vs), max(v.y for v in vs),
            min(v.z for v in vs), max(v.z for v in vs))

deska = B.deska()
deska.name = "Motherboard"
_, _, _, _, dz0, _ = obalka(deska)
# posadit desku tak, aby horni plocha PCB byla presne v z = 0
deska.location.z = -(dz0 + 0.0035)
bpy.context.view_layer.update()

def montovat(nazev, fn, x, y, rot=(0, 0, 0), mezera=0.0):
    o = fn()
    o.name = nazev
    o.rotation_euler = rot
    bpy.context.view_layer.update()
    x0, x1, y0, y1, z0, z1 = obalka(o)
    # spodek dilu dosedne na desku, stred na zadane misto slotu
    o.location = (x - (x0 + x1) / 2.0 + o.location.x,
                  y - (y0 + y1) / 2.0 + o.location.y,
                  mezera - z0 + o.location.z)
    bpy.context.view_layer.update()
    return o

dily = [deska]
# patice CPU
cpu = montovat("Cpu", B.procesor, -0.018, 0.018)
dily.append(cpu)
# chladic sedi na procesoru, ne na desce
dily.append(montovat("Cooler", B.chladic, -0.018, 0.018, mezera=obalka(cpu)[5]))
# dva moduly pameti stoji ve slotech kolmo k desce
for i, x in enumerate((0.026, 0.042)):
    dily.append(montovat("Ram%d" % i, B.pamet, x, 0.018,
                         rot=(math.radians(90), 0, math.radians(90))))
# grafika lezi rovnobezne s deskou, konektorem ve slotu PCIe
dily.append(montovat("Gpu", B.grafika, 0.004, -0.027, mezera=0.004))
# SSD do slotu M.2
dily.append(montovat("Ssd", B.disk, -0.006, -0.008))

# zdroj se na desku nemontuje - stoji vedle, jako v otevrene sestave
psu = B.zdroj(); psu.name = "Psu"
bpy.context.view_layer.update()
x0, x1, y0, y1, z0, z1 = obalka(psu)
psu.location = (0.048 - (x0 + x1) / 2.0, -0.092 - (y0 + y1) / 2.0, -z0 - 0.006)
dily.append(psu)

mat = bpy.data.materials.new("Dil")
mat.use_nodes = True
b = mat.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.30, 0.32, 0.36, 1)
b.inputs["Roughness"].default_value = 0.42
b.inputs["Metallic"].default_value = 0.25
for o in dily:
    o.data.materials.append(mat)

sv = bpy.data.worlds.new("Pozadi")
sv.use_nodes = True
sv.node_tree.nodes["Background"].inputs["Color"].default_value = (0.045, 0.05, 0.065, 1)
bpy.context.scene.world = sv

bpy.ops.object.camera_add(location=(0.09, -0.30, 0.20), rotation=(math.radians(58), 0, math.radians(17)))
kam = bpy.context.object
kam.data.type = 'ORTHO'
kam.data.ortho_scale = 0.30
bpy.context.scene.camera = kam

for loc, e, s in (((0.25, -0.28, 0.35), 16, 0.4), ((-0.30, -0.20, 0.18), 6, 0.7), ((0.0, 0.25, 0.25), 5, 0.8)):
    bpy.ops.object.light_add(type='AREA', location=loc)
    bpy.context.object.data.energy = e
    bpy.context.object.data.size = s

s = bpy.context.scene
dostupne = [p.identifier for p in s.render.bl_rna.properties['engine'].enum_items]
for j in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'CYCLES'):
    if j in dostupne:
        s.render.engine = j
        break
s.render.resolution_x = 1400
s.render.resolution_y = 900
s.render.filepath = CIL
bpy.ops.render.render(write_still=True)
print("NAHLED " + CIL)
