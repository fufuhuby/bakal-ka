# Generuje low-poly modely komponent PC pro sestavovaci ulohu.
#
# Spousti se headless:
#   blender -b -P Tools/build_pc_parts.py
#
# Vystup: Assets/_BP/Models/PC/<Dil>.fbx + nahledovy render.
#
# PROC PROCEDURALNE A NE RUCNE: modely se budou jeste ladit (velikosti,
# citelnost siluety) a rucni model by se pri kazde zmene delal znovu.
# Skript je zaroven doklad, jak dily vznikly - to se do prace hodi.

import bpy, os, sys, math, json

SEM = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SEM)
from pc_parts_lib import *

KOREN = os.path.dirname(SEM)
VYSTUP = os.path.join(KOREN, "Assets", "_BP", "Models", "PC")
os.makedirs(VYSTUP, exist_ok=True)

# Cilove nejvetsi hrany v metrech. Poradi velikosti odpovida skutecnosti,
# ale rozsah je stlaceny do 3,8-12 cm: v realnem pomeru by RAM proti desce
# byla 1,7 cm a v MR by se spatne chytala i spatne poznavala.
VELIKOSTI = {
    "Motherboard": 0.120,
    "Gpu":         0.095,
    "Ram":         0.070,
    "Psu":         0.062,
    "Cooler":      0.058,
    "Ssd":         0.050,
    "Cpu":         0.038,
}


def deska():
    d = []
    d.append(kvadr("pcb", (0.120, 0.100, 0.0035), (0, 0, 0), zkoseni=0.0015))

    # patice CPU - vyvyseny ramecek, aby bylo poznat, kam CPU patri
    for dx, dy, sx, sy in ((0, 0.017, 0.036, 0.003), (0, -0.017, 0.036, 0.003),
                           (-0.0165, 0, 0.003, 0.037), (0.0165, 0, 0.003, 0.037)):
        d.append(kvadr("patice", (sx, sy, 0.004), (-0.018 + dx, 0.018 + dy, 0.0035)))

    # sloty na pamet
    slot = kvadr("slot_ram", (0.004, 0.050, 0.005), (0.026, 0.018, 0.004), zkoseni=0.0006)
    d.append(rada(slot, 4, (0.008, 0, 0)))

    # slot PCIe pro grafiku
    d.append(kvadr("slot_pcie", (0.062, 0.005, 0.005), (0.004, -0.024, 0.004), zkoseni=0.0006))

    # slot M.2 pro SSD
    d.append(kvadr("slot_m2", (0.042, 0.003, 0.002), (-0.006, -0.008, 0.0028)))

    # chladic cipsetu
    d.append(kvadr("chipset", (0.020, 0.020, 0.005), (0.035, -0.032, 0.004), zkoseni=0.0012))

    # zadni panel konektoru - jednoznacne urcuje, kterym smerem deska lezi
    d.append(kvadr("io_panel", (0.044, 0.006, 0.014), (-0.036, 0.045, 0.0085), zkoseni=0.001))

    # napajeci konektor 24 pin
    d.append(kvadr("napajeni", (0.006, 0.024, 0.007), (0.055, 0.030, 0.005), zkoseni=0.0008))

    # montazni sloupky v rozich
    for sx in (-1, 1):
        for sy in (-1, 1):
            d.append(valec("sloupek", 0.0035, 0.004, (sx * 0.053, sy * 0.043, 0.0035), stran=10))

    return dokoncit(spojit("Motherboard", d), VELIKOSTI["Motherboard"])


def procesor():
    d = []
    d.append(kvadr("substrat", (0.038, 0.038, 0.0030), (0, 0, 0), zkoseni=0.0008))
    d.append(kvadr("ihs", (0.027, 0.027, 0.0028), (0, 0, 0.0029), zkoseni=0.0012, segmenty=2))
    # znacka orientace v rohu - u procesoru je to jediny zachytny bod
    d.append(valec("znacka", 0.0022, 0.0016, (-0.0145, -0.0145, 0.0016), stran=8))
    return dokoncit(spojit("Cpu", d), VELIKOSTI["Cpu"])


def chladic():
    d = []
    d.append(kvadr("zaklad", (0.034, 0.034, 0.006), (0, 0, 0.003), zkoseni=0.001))

    # zebra - pole tenkych plechu, typicka silueta vezoveho chladice
    zebro = kvadr("zebro", (0.044, 0.040, 0.0012), (0, 0, 0.020))
    d.append(rada(zebro, 14, (0, 0, 0.0028)))

    # heatpipy
    for x in (-0.011, -0.004, 0.004, 0.011):
        d.append(valec("heatpipe", 0.0022, 0.048, (x, 0, 0.030), stran=10))

    # ventilator na CELNI strane (-Y): schovany za zebry neni z vetsiny
    # pohledu videt a chladic pak vypada jen jako stoh plechu
    d.append(ventilator("vent", 0.021, 0.008, (0, -0.028, 0.030), listu=7))
    return dokoncit(spojit("Cooler", d), VELIKOSTI["Cooler"])


def pamet():
    d = []
    d.append(kvadr("pcb", (0.070, 0.020, 0.0016), (0, 0, 0)))

    # chladic pameti se zubatym hrebenem - odlisuje RAM od SSD,
    # oboji je jinak jen tenka desticka
    d.append(kvadr("chladic", (0.066, 0.015, 0.0042), (0, 0.0016, 0), zkoseni=0.0006))
    zub = kvadr("zub", (0.004, 0.004, 0.0040), (-0.028, 0.0095, 0))
    d.append(rada(zub, 8, (0.008, 0, 0)))

    # kontakty a klicova drazka na spodni hrane
    kontakt = kvadr("kontakt", (0.0016, 0.004, 0.0018), (-0.030, -0.0105, 0))
    d.append(rada(kontakt, 12, (0.0055, 0, 0)))
    d.append(kvadr("klic", (0.0022, 0.005, 0.0026), (0.004, -0.0100, 0)))

    return dokoncit(spojit("Ram", d), VELIKOSTI["Ram"])


def grafika():
    d = []
    d.append(kvadr("pcb", (0.092, 0.030, 0.0018), (0, 0, 0)))
    d.append(kvadr("kryt", (0.090, 0.032, 0.0110), (0.002, 0.002, 0.0064), zkoseni=0.0016))
    d.append(kvadr("backplate", (0.086, 0.028, 0.0016), (0.002, 0.001, -0.0017)))

    for x in (-0.020, 0.024):
        d.append(ventilator("vent", 0.0135, 0.0035, (x, 0.002, 0.0122), listu=9))

    # drzak do skrine s vyrezy - druhy jednoznacny znak grafiky
    d.append(kvadr("drzak", (0.0018, 0.030, 0.026), (-0.047, 0.001, 0.0075)))
    for z in (0.001, 0.010):
        d.append(kvadr("vyrez", (0.0030, 0.014, 0.0035), (-0.049, 0.001, z)))

    # kontaktni hrebinek do slotu PCIe
    d.append(kvadr("konektor", (0.048, 0.005, 0.0030), (0.010, -0.0158, 0)))
    d.append(kvadr("klic", (0.0022, 0.006, 0.0034), (-0.008, -0.0160, 0)))

    return dokoncit(spojit("Gpu", d), VELIKOSTI["Gpu"])


def disk():
    d = []
    d.append(kvadr("pcb", (0.050, 0.011, 0.0012), (0, 0, 0)))
    for x in (-0.008, 0.008):
        d.append(kvadr("cip", (0.011, 0.008, 0.0014), (x, 0, 0.0013), zkoseni=0.0003))
    d.append(kvadr("radic", (0.007, 0.007, 0.0013), (-0.020, 0, 0.0012), zkoseni=0.0003))
    # uzka kontaktni hrana s drazkou - odlisi SSD od pameti
    d.append(kvadr("kontakt", (0.010, 0.0035, 0.0014), (0.0238, 0, 0)))
    return dokoncit(spojit("Ssd", d), VELIKOSTI["Ssd"])


def zdroj():
    d = []
    d.append(kvadr("telo", (0.062, 0.045, 0.038), (0, 0, 0), zkoseni=0.0022, segmenty=2))
    d.append(ventilator("vent", 0.019, 0.005, (0, 0, 0.0195), listu=9))

    # sitova zasuvka a vypinac na zadni strane
    d.append(kvadr("zasuvka", (0.014, 0.0025, 0.011), (-0.014, -0.0232, -0.004), zkoseni=0.0008))
    d.append(kvadr("vypinac", (0.009, 0.0025, 0.006), (0.006, -0.0232, -0.004)))

    # svazek kabelu z celni strany
    for i, (y, z) in enumerate(((-0.008, 0.008), (0.000, 0.010), (0.008, 0.007))):
        d.append(valec("kabel", 0.0035, 0.016, (0.0305 + 0.006, y, z), stran=8, osa='X'))

    return dokoncit(spojit("Psu", d), VELIKOSTI["Psu"])


STAVITELE = [
    ("Motherboard", deska),
    ("Cpu", procesor),
    ("Cooler", chladic),
    ("Ram", pamet),
    ("Gpu", grafika),
    ("Ssd", disk),
    ("Psu", zdroj),
]


def main():
    zprava = []
    for nazev, fn in STAVITELE:
        cistit_scenu()
        o = fn()
        o.name = nazev
        zprava.append(statistika(o))

        cesta = os.path.join(VYSTUP, nazev + ".fbx")
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.export_scene.fbx(
            filepath=cesta,
            use_selection=True,
            object_types={'MESH'},
            apply_scale_options='FBX_SCALE_UNITS',
            global_scale=1.0,
            axis_forward='-Z',
            axis_up='Y',
            mesh_smooth_type='FACE',
            use_mesh_modifiers=True,
            bake_space_transform=False,
        )
        zprava[-1]["fbx"] = os.path.basename(cesta)

    print("VYSLEDEK_JSON " + json.dumps(zprava, ensure_ascii=False))


main()
