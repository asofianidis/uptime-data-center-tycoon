"""
Generates the Uptime equipment models and exports them as FBX into Assets/Resources/Models.

Run headless:
  blender -b --factory-startup -P Tools/blender/make_models.py -- <output_dir>

Conventions: 1 unit = 1 m, origin at the footprint center on the floor, Z up, front faces -Y.
Exported with baked axis conversion so Unity sees Y up and the front facing -Z.
"""
import bpy, bmesh, math, os, sys

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.path.join(os.getcwd(), "Assets", "Resources", "Models")
os.makedirs(OUT, exist_ok=True)

MATS = {}

def mat(name, rgb, emit=0.0, rough=0.5, metal=0.0):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit > 0:
        bsdf.inputs["Emission Color"].default_value = (*rgb, 1)
        bsdf.inputs["Emission Strength"].default_value = emit
    MATS[name] = m
    return m

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MATS.clear()

PARTS = []

def _finish(obj, material, bevel):
    obj.data.materials.append(material)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
    PARTS.append(obj)
    return obj

def box(x, y, z, sx, sy, sz, material, bevel=0.0, rot=(0, 0, 0)):
    """Box with its bottom-center... no: center at (x,y,z), full size (sx,sy,sz)."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x, y, z), rotation=rot)
    o = bpy.context.active_object
    o.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, material, bevel)

def cyl(x, y, z, r, h, material, verts=16, bevel=0.0, r2=None, rot=(0, 0, 0)):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=(x, y, z), rotation=rot)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=(x, y, z), rotation=rot)
    o = bpy.context.active_object
    return _finish(o, material, bevel)

def sphere(x, y, z, r, material, seg=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=r, location=(x, y, z))
    o = bpy.context.active_object
    bpy.ops.object.shade_smooth()
    return _finish(o, material, 0)

def export(name):
    for o in PARTS:
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        for m in o.modifiers:
            bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.select_all(action="DESELECT")
    for o in PARTS:
        o.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True, axis_forward="-Z", axis_up="Y", object_types={"MESH"},
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
    )
    print("exported", path)
    PARTS.clear()

# ------------------------------------------------------------------ shared palette
def P():
    return dict(
        steel=mat("Steel", (0.55, 0.57, 0.6), rough=0.35, metal=0.6),
        dark=mat("DarkPanel", (0.07, 0.08, 0.09), rough=0.45),
        black=mat("Black", (0.02, 0.02, 0.025), rough=0.6),
        white=mat("Housing", (0.85, 0.87, 0.88), rough=0.4),
        grille=mat("Grille", (0.18, 0.19, 0.21), rough=0.6, metal=0.3),
        green=mat("LedGreen", (0.1, 1.0, 0.3), emit=4),
        blue=mat("LedBlue", (0.2, 0.6, 1.0), emit=4),
        amber=mat("LedAmber", (1.0, 0.6, 0.1), emit=4),
        red=mat("LedRed", (1.0, 0.1, 0.05), emit=4),
        screen=mat("Screen", (0.2, 0.45, 0.85), emit=2),
        yellow=mat("SafetyYellow", (0.95, 0.75, 0.05), rough=0.5),
        rubber=mat("Rubber", (0.04, 0.04, 0.04), rough=0.9),
    )

# ------------------------------------------------------------------ models
def make_crac(name="crac", h=1.8, color=(0.85, 0.87, 0.88)):
    reset(); p = P()
    body = mat("CracBody", color, rough=0.4)
    box(0, 0, 0.04, 0.86, 0.82, 0.08, p["black"])
    box(0, 0, h / 2 + 0.04, 0.9, 0.86, h - 0.02, body, bevel=0.02)
    for i in range(9):
        box(0, -0.435, 0.25 + i * 0.1, 0.76, 0.02, 0.045, p["grille"], bevel=0.005)
    box(0, -0.44, h - 0.25, 0.42, 0.02, 0.2, p["dark"], bevel=0.01)
    box(0.0, -0.452, h - 0.25, 0.34, 0.01, 0.12, p["blue"])
    box(-0.12, -0.452, h - 0.4, 0.04, 0.01, 0.04, p["green"])
    for x in (-0.21, 0.21):
        cyl(x, 0, h + 0.07, 0.18, 0.04, p["grille"], verts=24, bevel=0.005)
        cyl(x, 0, h + 0.09, 0.05, 0.03, p["steel"], verts=12)
        for a in range(3):
            box(x, 0, h + 0.09, 0.3, 0.05, 0.01, p["steel"], rot=(0, 0, a * math.pi / 3))
    export(name)

def make_core():
    reset(); p = P()
    frame = mat("CoreFrame", (0.1, 0.11, 0.14), rough=0.4, metal=0.3)
    trim = mat("CoreTrim", (0.15, 0.35, 0.75), rough=0.35)
    box(0, 0, 1.0, 0.7, 0.95, 2.0, frame, bevel=0.015)
    box(0, -0.47, 1.02, 0.64, 0.02, 1.86, mat("Glass", (0.05, 0.07, 0.1), rough=0.1, metal=0.5), bevel=0.005)
    for r in range(8):
        z = 0.35 + r * 0.19
        box(0, -0.485, z, 0.56, 0.015, 0.12, trim, bevel=0.003)
        for i in range(12):
            box(-0.24 + i * 0.044, -0.495, z + 0.03, 0.02, 0.006, 0.018, p["green"] if (i * 7 + r * 3) % 9 else p["amber"])
    box(0, 0, 2.01, 0.6, 0.85, 0.02, trim)
    box(0, -0.2, 2.025, 0.3, 0.2, 0.01, p["blue"])
    for x in (-0.25, 0.25):
        for y in (-0.38, 0.38):
            cyl(x, y, 0.025, 0.03, 0.05, p["rubber"], verts=8)
    export("core")

def make_isp():
    reset(); p = P()
    body = mat("IspBody", (0.55, 0.57, 0.6), rough=0.45, metal=0.2)
    orange = mat("FiberOrange", (1.0, 0.55, 0.12), rough=0.5)
    box(0, 0.1, 0.65, 0.75, 0.55, 1.3, body, bevel=0.02)
    box(0, -0.18, 0.9, 0.62, 0.02, 0.55, p["dark"], bevel=0.01)
    box(0, -0.195, 1.1, 0.42, 0.01, 0.04, orange)
    for i in range(6):
        box(-0.2 + i * 0.08, -0.195, 0.85, 0.03, 0.01, 0.03, p["green"])
    cyl(0, 0.1, 1.36, 0.2, 0.1, mat("Spool", (1.0, 0.85, 0.1), rough=0.6), verts=24)
    cyl(0, 0.1, 1.42, 0.08, 0.02, p["steel"], verts=16)
    for i in range(4):
        cyl(-0.24 + i * 0.16, 0.36, 1.0, 0.012, 0.6, orange, verts=6, rot=(0.25, 0, 0))
    export("isp")

def make_xfmr():
    reset(); p = P()
    tank = mat("TransformerGreen", (0.42, 0.52, 0.43), rough=0.55)
    box(0, 0, 0.05, 0.95, 0.95, 0.1, mat("Concrete", (0.55, 0.55, 0.53), rough=0.95))
    box(0, 0, 0.66, 0.62, 0.62, 1.1, tank, bevel=0.03)
    for i in range(7):
        y = -0.27 + i * 0.09
        box(0.36, y, 0.62, 0.1, 0.025, 0.9, tank)
        box(-0.36, y, 0.62, 0.1, 0.025, 0.9, tank)
    for i in (-1, 0, 1):
        cyl(i * 0.17, 0, 1.27, 0.05, 0.12, mat("Bushing", (0.55, 0.32, 0.22), rough=0.3), verts=12)
        for k in range(4):
            cyl(i * 0.17, 0, 1.36 + k * 0.06, 0.065, 0.025, mat("Bushing", (0.55, 0.32, 0.22)), verts=12)
    box(0, -0.315, 0.75, 0.2, 0.01, 0.2, p["yellow"])
    box(0, -0.32, 0.75, 0.06, 0.005, 0.12, p["black"], rot=(0, math.radians(20), 0))
    export("xfmr")

def make_gen():
    reset(); p = P()
    yellow = mat("GenYellow", (0.92, 0.72, 0.1), rough=0.45)
    box(0, 0, 0.08, 1.85, 0.85, 0.16, p["black"], bevel=0.01)
    box(0, 0, 0.16 + 0.58, 1.75, 0.76, 1.16, yellow, bevel=0.03)
    for i in range(7):
        box(-0.65 + i * 0.13, -0.385, 0.85, 0.07, 0.015, 0.55, p["grille"])
    for x in (0.35, 0.75):
        box(x, -0.39, 0.7, 0.03, 0.03, 0.12, p["steel"])
    box(0.55, -0.385, 1.15, 0.25, 0.01, 0.12, p["dark"])
    box(0.55, -0.39, 1.15, 0.15, 0.005, 0.06, p["green"])
    cyl(0.6, 0.1, 1.55, 0.08, 0.5, p["steel"], verts=16)
    cyl(0.6, 0.1, 1.82, 0.1, 0.06, p["dark"], verts=16)
    box(-0.4, 0, 1.33, 0.6, 0.45, 0.02, p["grille"])
    export("gen")

def make_ups():
    reset(); p = P()
    body = mat("UpsBody", (0.17, 0.19, 0.22), rough=0.4)
    box(0, 0, 0.85, 0.9, 0.75, 1.7, body, bevel=0.02)
    for i in range(7):
        box(0, -0.38, 0.22 + i * 0.19, 0.78, 0.02, 0.15, mat("Battery", (0.3, 0.32, 0.36), rough=0.5), bevel=0.005)
        box(0.3, -0.392, 0.22 + i * 0.19, 0.04, 0.005, 0.04, p["green"])
    box(-0.15, -0.39, 1.55, 0.35, 0.01, 0.16, p["screen"])
    box(0, 0, 1.705, 0.8, 0.6, 0.01, mat("UpsTop", (0.2, 0.75, 0.35), rough=0.5))
    export("ups")

def make_swgear():
    reset(); p = P()
    body = mat("SwitchgearGray", (0.6, 0.6, 0.62), rough=0.45, metal=0.2)
    box(0, 0.1, 0.95, 0.92, 0.6, 1.9, body, bevel=0.02)
    for x in (-0.23, 0.23):
        box(x, -0.205, 0.95, 0.42, 0.02, 1.75, mat("SwitchgearDoor", (0.66, 0.66, 0.68), rough=0.4), bevel=0.01)
        box(x + 0.15, -0.22, 0.95, 0.03, 0.03, 0.2, p["steel"])
        cyl(x - 0.05, -0.22, 1.45, 0.06, 0.02, p["white"], verts=16, rot=(math.pi / 2, 0, 0))
        box(x - 0.1, -0.22, 1.25, 0.03, 0.01, 0.03, p["green"])
        box(x, -0.22, 1.25, 0.03, 0.01, 0.03, p["red"])
    box(0, 0.1, 1.905, 0.85, 0.5, 0.01, p["yellow"])
    export("swgear")

def make_desk():
    reset(); p = P()
    wood = mat("Wood", (0.55, 0.4, 0.27), rough=0.6)
    box(0, 0.05, 0.74, 0.92, 0.56, 0.04, wood, bevel=0.01)
    for x in (-0.42, 0.42):
        box(x, 0.05, 0.36, 0.04, 0.5, 0.72, mat("WoodDark", (0.38, 0.27, 0.18)))
    box(0, 0.22, 0.79, 0.12, 0.08, 0.02, p["dark"])
    box(0, 0.24, 0.84, 0.03, 0.03, 0.1, p["dark"])
    box(0, 0.22, 0.99, 0.48, 0.03, 0.29, p["black"], bevel=0.008)
    box(0, 0.205, 0.99, 0.44, 0.005, 0.25, p["screen"])
    box(0, -0.02, 0.765, 0.32, 0.12, 0.012, p["dark"])
    chair = mat("ChairFabric", (0.15, 0.17, 0.23), rough=0.8)
    box(0, -0.38, 0.47, 0.42, 0.4, 0.07, chair, bevel=0.02)
    box(0, -0.57, 0.75, 0.4, 0.06, 0.5, chair, bevel=0.02)
    cyl(0, -0.38, 0.24, 0.03, 0.42, p["steel"], verts=8)
    for a in range(5):
        ang = a * 2 * math.pi / 5
        box(math.cos(ang) * 0.13, -0.38 + math.sin(ang) * 0.13, 0.04, 0.26, 0.03, 0.03, p["dark"], rot=(0, 0, ang))
    export("desk")

def make_solar():
    reset(); p = P()
    cell = mat("SolarCell", (0.08, 0.14, 0.35), rough=0.15, metal=0.4)
    for row in (-0.5, 0.5):
        for x in (-0.65, 0.65):
            cyl(x, row + 0.15, 0.35, 0.03, 0.7, p["steel"], verts=8)
            cyl(x, row - 0.2, 0.2, 0.03, 0.4, p["steel"], verts=8)
        tilt = math.radians(-25)
        box(0, row, 0.62, 1.85, 0.8, 0.04, p["steel"], rot=(tilt, 0, 0))
        box(0, row - 0.003, 0.645, 1.8, 0.75, 0.02, cell, rot=(tilt, 0, 0))
        for i in range(1, 6):
            box(-0.9 + i * 0.3, row - 0.004, 0.66, 0.008, 0.74, 0.004, p["white"], rot=(tilt, 0, 0))
    export("solar")

def make_suppress():
    reset(); p = P()
    red = mat("SuppressRed", (0.85, 0.12, 0.1), rough=0.35)
    for i in range(3):
        x = -0.25 + i * 0.25
        cyl(x, 0.1, 0.65, 0.11, 1.3, red, verts=20, bevel=0.01)
        sphere(x, 0.1, 1.3, 0.11, red)
        cyl(x, 0.1, 1.45, 0.03, 0.1, p["steel"], verts=10)
    cyl(0, 0.1, 1.58, 0.025, 0.7, p["steel"], verts=10, rot=(0, math.pi / 2, 0))
    box(0, -0.15, 0.9, 0.3, 0.05, 0.35, p["white"], bevel=0.01)
    box(0, -0.18, 1.0, 0.06, 0.01, 0.06, p["green"])
    box(0, -0.18, 0.85, 0.12, 0.01, 0.05, p["red"])
    export("suppress")

def make_person():
    reset(); p = P()
    shirt = mat("Shirt", (0.2, 0.55, 1.0), rough=0.7)
    skin = mat("Skin", (0.86, 0.68, 0.55), rough=0.7)
    pants = mat("Pants", (0.18, 0.2, 0.26), rough=0.8)
    for x in (-0.07, 0.07):
        box(x, 0, 0.22, 0.09, 0.1, 0.44, pants, bevel=0.02)
        box(x, -0.02, 0.03, 0.1, 0.15, 0.06, p["rubber"], bevel=0.01)
    box(0, 0, 0.63, 0.28, 0.16, 0.4, shirt, bevel=0.04)
    for x in (-0.18, 0.18):
        box(x, 0, 0.62, 0.07, 0.08, 0.34, shirt, bevel=0.025)
        sphere(x, 0, 0.43, 0.04, skin, seg=8)
    sphere(0, 0, 0.94, 0.1, skin, seg=14)
    box(0, 0.01, 1.03, 0.2, 0.2, 0.05, mat("Cap", (0.12, 0.13, 0.15), rough=0.7), bevel=0.02)
    box(0, -0.1, 1.01, 0.16, 0.08, 0.015, mat("Cap", (0.12, 0.13, 0.15)))
    box(0, -0.085, 0.68, 0.1, 0.01, 0.12, p["white"])
    export("person")

make_crac("crac", 1.8, (0.85, 0.87, 0.88))
make_crac("inrow", 2.0, (0.75, 0.85, 0.95))
make_core()
make_isp()
make_xfmr()
make_gen()
make_ups()
make_swgear()
make_desk()
make_solar()
make_suppress()
make_person()
print("ALL MODELS DONE")
