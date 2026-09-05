# Pomocne funkce pro stavbu low-poly dilu PC.
# Blender je Z-up, Unity Y-up; prevod resi az FBX exporter.
# Vsechny rozmery jsou v METRECH a odpovidaji cilove velikosti v Unity.

import bpy, bmesh, math
from mathutils import Vector

def cistit_scenu():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for blok in (bpy.data.meshes, bpy.data.materials, bpy.data.objects):
        for d in list(blok):
            if d.users == 0:
                blok.remove(d)

def kvadr(nazev, velikost, pozice=(0, 0, 0), zkoseni=0.0, segmenty=1):
    """Kvadr o dane velikosti (x, y, z) se stredem v pozice."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=pozice)
    o = bpy.context.object
    o.name = nazev
    o.scale = Vector(velikost)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if zkoseni > 0:
        m = o.modifiers.new("zkoseni", 'BEVEL')
        m.width = zkoseni
        m.segments = segmenty
        m.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier=m.name)
    return o

def valec(nazev, polomer, vyska, pozice=(0, 0, 0), stran=16, osa='Z'):
    bpy.ops.mesh.primitive_cylinder_add(radius=polomer, depth=vyska,
                                        vertices=stran, location=pozice)
    o = bpy.context.object
    o.name = nazev
    if osa == 'X':
        o.rotation_euler[1] = math.pi / 2
    elif osa == 'Y':
        o.rotation_euler[0] = math.pi / 2
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return o

def rada(objekt, pocet, posun):
    """Pole kopii - pouziva se na chladici zebra a kontakty."""
    m = objekt.modifiers.new("pole", 'ARRAY')
    m.count = pocet
    m.use_relative_offset = False
    m.use_constant_offset = True
    m.constant_offset_displace = posun
    bpy.context.view_layer.objects.active = objekt
    bpy.ops.object.modifier_apply(modifier=m.name)
    return objekt

def ventilator(nazev, polomer, tloustka, pozice, listu=7):
    """Zjednoduseny ventilator: prstenec ramu, naboj a ploche listy.
    Modeluje se geometrii, ne texturou - dil musi byt poznat i jednobarevny."""
    dily = []

    ram = kvadr(nazev + "_ram", (polomer * 2, polomer * 2, tloustka), pozice, zkoseni=polomer * 0.12)
    dily.append(ram)

    # vyhloubeni ramu resi prstenec misto booleanu - boolean na tenkem
    # kvadru vyrabi degenerovane trojuhelniky a v Unity pak sviti svetlo divne
    prstenec = valec(nazev + "_prstenec", polomer * 0.92, tloustka * 1.2, pozice, stran=24)
    prstenec.name = nazev + "_prstenec"
    bpy.context.view_layer.objects.active = ram
    b = ram.modifiers.new("dira", 'BOOLEAN')
    b.operation = 'DIFFERENCE'
    b.object = prstenec
    bpy.ops.object.modifier_apply(modifier=b.name)
    bpy.data.objects.remove(prstenec, do_unlink=True)

    naboj = valec(nazev + "_naboj", polomer * 0.26, tloustka * 0.9, pozice, stran=16)
    dily.append(naboj)

    for i in range(listu):
        uhel = i * 2 * math.pi / listu
        r = polomer * 0.58
        p = (pozice[0] + math.cos(uhel) * r,
             pozice[1] + math.sin(uhel) * r,
             pozice[2])
        list_ = kvadr(nazev + "_list%d" % i, (polomer * 0.62, polomer * 0.20, tloustka * 0.35), p)
        list_.rotation_euler[2] = uhel
        list_.rotation_euler[1] = math.radians(18)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        dily.append(list_)

    return spojit(nazev, dily)

def spojit(nazev, objekty):
    objekty = [o for o in objekty if o is not None]
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objekty:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objekty[0]
    if len(objekty) > 1:
        bpy.ops.object.join()
    o = bpy.context.object
    o.name = nazev
    return o

def dokoncit(objekt, cilova_delka=None, hladce=30.0):
    """Vycisti mesh, posadi puvod do stredu obalky a volitelne premeri
    na cilovou nejvetsi hranu. Puvod ve stredu je nutny - validator
    porovnava stredy objektu s cilovymi pozicemi sablony."""
    bpy.context.view_layer.objects.active = objekt
    objekt.select_set(True)

    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(objekt.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.00005)
    bmesh.update_edit_mesh(objekt.data)
    bpy.ops.object.mode_set(mode='OBJECT')

    bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')

    if cilova_delka:
        d = objekt.dimensions
        nej = max(d.x, d.y, d.z)
        if nej > 0:
            k = cilova_delka / nej
            objekt.scale = (k, k, k)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    if hasattr(objekt.data, "use_auto_smooth"):   # do 4.0; v novejsich verzich uz neexistuje
        objekt.data.use_auto_smooth = True
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(hladce))
    return objekt

def statistika(objekt):
    return {
        "nazev": objekt.name,
        "trojuhelniku": sum(len(p.vertices) - 2 for p in objekt.data.polygons),
        "rozmery_cm": [round(v * 100, 1) for v in objekt.dimensions],
    }
