"""
Generates the Uptime equipment models and exports them as FBX into Assets/Resources/Models.

Run headless:
  blender -b --factory-startup -P Tools/blender/make_models.py -- <output_dir> [model names...]

Conventions: 1 unit = 1 m, origin at the footprint center on the floor, Z up, front faces -Y.
Exported with baked axis conversion so Unity sees Y up and the front facing -Z.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Matrix, Vector

_ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = _ARGS[0] if _ARGS else os.path.join(os.getcwd(), "Assets", "Resources", "Models")
ONLY = set(_ARGS[1:])   # optional model names to (re)build; empty = all
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

def rod(p0, p1, r, material, verts=8):
    """Cylinder from point p0 to point p1 (pipes, wires, struts)."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length, location=(a + b) / 2)
    o = bpy.context.active_object
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = d.to_track_quat("Z", "Y")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return _finish(o, material, 0)

def blob(x, y, z, sx, sy, sz, material, subdiv=1, jitter=0.12, seed=0):
    """Faceted low-poly lump (tree canopies, rocks, bushes)."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdiv, radius=1, location=(x, y, z))
    o = bpy.context.active_object
    rnd = random.Random(seed)
    for v in o.data.vertices:
        v.co *= 1 + rnd.uniform(-jitter, jitter)
    o.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, material, 0)

def cone(x, y, z, r, h, material, verts=8, jitter=0.0, seed=0, r2=0.0):
    """Cone with its base at z (unlike cyl(), which is centered)."""
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=(x, y, z + h / 2))
    o = bpy.context.active_object
    if jitter > 0:
        rnd = random.Random(seed)
        for v in o.data.vertices:
            if v.co.z < 0:
                v.co.x *= 1 + rnd.uniform(-jitter, jitter)
                v.co.y *= 1 + rnd.uniform(-jitter, jitter)
                v.co.z += rnd.uniform(-jitter, jitter) * h * 0.3
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
    # The joined object inherits the first part's transform; bake it into the mesh so the pivot is
    # the footprint center on the floor (the game treats the model root as the tile anchor).
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
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

# ------------------------------------------------------------------ large / late-game equipment
def make_liquid():
    """Liquid cooling CDU, 2x1 tiles."""
    reset(); p = P()
    body = mat("CduBody", (0.82, 0.86, 0.9), rough=0.4)
    door = mat("CduDoor", (0.88, 0.91, 0.94), rough=0.35)
    accent = mat("CduAccent", (0.2, 0.55, 0.95), rough=0.4)
    supply = mat("PipeSupply", (0.18, 0.4, 0.95), rough=0.35, metal=0.3)
    ret = mat("PipeReturn", (0.92, 0.25, 0.18), rough=0.35, metal=0.3)
    box(0, 0, 0.04, 1.78, 0.78, 0.08, p["black"])
    box(0, 0, 0.73, 1.82, 0.8, 1.3, body, bevel=0.02)
    for x in (-0.45, 0.45):
        box(x, -0.405, 0.75, 0.86, 0.02, 1.18, door, bevel=0.008)
        box(x + (0.38 if x < 0 else -0.38), -0.425, 0.8, 0.025, 0.025, 0.24, p["steel"])
        for i in range(6):
            box(x, -0.418, 0.22 + i * 0.05, 0.7, 0.012, 0.022, p["grille"])
    box(0, -0.415, 1.29, 1.8, 0.012, 0.045, accent)
    # Gauges on the left door, touchscreen on the right
    for k, gx in enumerate((-0.62, -0.42)):
        cyl(gx, -0.42, 1.06, 0.065, 0.02, p["white"], verts=20, rot=(math.pi / 2, 0, 0))
        box(gx, -0.432, 1.08, 0.006, 0.004, 0.05, p["red"] if k else p["black"], rot=(0, math.radians(35 - k * 70), 0))
    box(0.55, -0.42, 1.02, 0.36, 0.015, 0.24, p["dark"], bevel=0.008)
    box(0.55, -0.43, 1.02, 0.31, 0.005, 0.19, mat("ScreenTeal", (0.2, 0.75, 0.9), emit=2.5))
    box(0.25, -0.425, 1.1, 0.03, 0.006, 0.03, p["green"])
    box(0.25, -0.425, 1.04, 0.03, 0.006, 0.03, p["amber"])
    # Supply/return headers on the roof with risers, flanges and valves
    for k, (m, y) in enumerate(((supply, -0.12), (ret, 0.16))):
        cyl(0, y, 1.62, 0.055, 1.6, m, verts=12, rot=(0, math.pi / 2, 0))
        for x in (-0.8, 0.8):
            sphere(x, y, 1.62, 0.055, m, seg=10)
        for x in (-0.55, 0.0, 0.55):
            cyl(x, y, 1.48, 0.045, 0.28, m, verts=12)
            cyl(x, y, 1.46, 0.07, 0.03, p["steel"], verts=12)
        cyl(-0.3 + k * 0.6, y, 1.71, 0.035, 0.12, p["steel"], verts=8)
        box(-0.3 + k * 0.6, y - 0.04, 1.78, 0.03, 0.14, 0.02, m)
    # Hoses dropping to the floor at the back
    for x, m in ((-0.75, supply), (-0.62, ret)):
        rod((x, 0.2, 1.62), (x, 0.42, 1.2), 0.035, m)
        rod((x, 0.42, 1.2), (x, 0.42, 0.1), 0.035, m)
    export("liquid")

def make_xfmr_big():
    """Substation feed, 2x2 tiles: transformer, gantry and fence."""
    reset(); p = P()
    tank = mat("TransformerGreen", (0.42, 0.52, 0.43), rough=0.55)
    concrete = mat("Concrete", (0.55, 0.55, 0.53), rough=0.95)
    gravel = mat("Gravel", (0.42, 0.41, 0.39), rough=1.0)
    porcelain = mat("Bushing", (0.55, 0.32, 0.22), rough=0.3)
    fence = mat("FenceSteel", (0.62, 0.64, 0.66), rough=0.5, metal=0.5)
    box(0, 0, 0.02, 1.94, 1.94, 0.04, gravel)
    box(0, 0.05, 0.1, 1.1, 0.95, 0.12, concrete, bevel=0.01)
    box(0, 0.05, 0.78, 0.86, 0.66, 1.24, tank, bevel=0.03)
    box(0, 0.05, 1.42, 0.92, 0.72, 0.05, tank, bevel=0.01)
    for side in (-1, 1):
        for i in range(8):
            box(side * 0.55, -0.22 + i * 0.075, 0.76, 0.2, 0.025, 1.0, tank)
        rod((side * 0.43, -0.25, 1.28), (side * 0.65, -0.25, 1.28), 0.02, tank)
    # Conservator tank
    cyl(0, 0.36, 1.68, 0.12, 0.62, tank, verts=16, rot=(0, math.pi / 2, 0))
    for x in (-0.2, 0.2):
        box(x, 0.36, 1.52, 0.04, 0.04, 0.22, p["steel"])
    # HV bushings with porcelain sheds
    bush_tops = []
    for x in (-0.26, 0.0, 0.26):
        cyl(x, -0.08, 1.56, 0.04, 0.26, porcelain, verts=10)
        for k in range(5):
            cyl(x, -0.08, 1.48 + k * 0.06, 0.07, 0.022, porcelain, verts=12)
        bush_tops.append((x, -0.08, 1.72))
    for x in (-0.18, 0.0, 0.18):
        cyl(x, -0.24, 1.5, 0.03, 0.12, porcelain, verts=8)
    # Overhead gantry with insulator strings feeding the bushings
    for x in (-0.82, 0.82):
        box(x, -0.08, 1.25, 0.08, 0.08, 2.5, p["steel"])
        box(x, -0.08, 0.06, 0.18, 0.18, 0.12, concrete)
    box(0, -0.08, 2.42, 1.74, 0.07, 0.07, p["steel"])
    for x, y, z in bush_tops:
        for k in range(4):
            cyl(x, y, 2.33 - k * 0.055, 0.045, 0.02, porcelain, verts=10)
        rod((x, y, 2.38), (x, y, 2.14), 0.012, p["steel"], verts=6)
        rod((x, y, 2.14), (x, y, z), 0.012, mat("Copper", (0.72, 0.45, 0.25), rough=0.3, metal=0.8), verts=6)
    # Control cabinet
    box(-0.68, 0.68, 0.55, 0.36, 0.28, 0.9, mat("SwitchgearGray", (0.6, 0.6, 0.62), rough=0.45, metal=0.2), bevel=0.01)
    box(-0.68, 0.535, 0.6, 0.3, 0.01, 0.7, mat("SwitchgearDoor", (0.66, 0.66, 0.68), rough=0.4))
    box(-0.6, 0.53, 0.75, 0.03, 0.006, 0.03, p["green"])
    # Perimeter fence with a gate on the front
    e = 0.94
    posts = [-e, -0.47, 0.0, 0.47, e]
    for t in posts:
        for (x, y) in ((t, -e), (t, e), (-e, t), (e, t)):
            cyl(x, y, 0.6, 0.022, 1.2, fence, verts=6)
    for z in (0.12, 0.6, 1.15):
        for side in (-e, e):
            rod((-e, side, z), (e, side, z), 0.01, fence, verts=4)
            rod((side, -e, z), (side, e, z), 0.01, fence, verts=4)
    for side in (-e, e):   # mesh infill as thin vertical wires
        for i in range(19):
            t = -e + (i + 0.5) * (2 * e / 19)
            if not (side == -e and -0.47 < t < 0.47):
                rod((t, side, 0.12), (t, side, 1.15), 0.004, fence, verts=3)
            rod((side, t, 0.12), (side, t, 1.15), 0.004, fence, verts=3)
    box(0, -e - 0.01, 0.75, 0.24, 0.01, 0.24, p["yellow"])
    box(0, -e - 0.016, 0.75, 0.05, 0.004, 0.15, p["black"], rot=(0, math.radians(20), 0))
    export("xfmr_big")

def make_swgear_big():
    """Main distribution board, 2x1 tiles: four-section lineup."""
    reset(); p = P()
    body = mat("SwitchgearGray", (0.58, 0.6, 0.63), rough=0.45, metal=0.2)
    door = mat("SwitchgearDoor", (0.66, 0.67, 0.7), rough=0.4)
    box(0, 0.08, 0.04, 1.84, 0.62, 0.08, p["black"])
    box(0, 0.08, 1.04, 1.84, 0.62, 1.92, body, bevel=0.015)
    for i in range(4):
        x = -0.69 + i * 0.46
        box(x, -0.235, 1.38, 0.43, 0.02, 1.1, door, bevel=0.006)
        box(x, -0.235, 0.44, 0.43, 0.02, 0.68, door, bevel=0.006)
        for k in range(7):
            box(x, -0.248, 0.2 + k * 0.07, 0.3, 0.008, 0.025, p["grille"])
        box(x, -0.25, 1.55, 0.26, 0.01, 0.34, p["dark"], bevel=0.004)
        box(x, -0.256, 1.55, 0.12, 0.012, 0.2, p["black"])
        box(x, -0.264, 1.6, 0.03, 0.008, 0.06, p["white"])
        cyl(x - 0.08, -0.25, 1.15, 0.055, 0.015, p["white"], verts=16, rot=(math.pi / 2, 0, 0))
        box(x + 0.06, -0.252, 1.15, 0.025, 0.006, 0.025, p["green"])
        box(x + 0.12, -0.252, 1.15, 0.025, 0.006, 0.025, p["red"])
        box(x + 0.18, -0.255, 1.0, 0.02, 0.025, 0.16, p["steel"])
        box(x, -0.248, 1.83, 0.14, 0.006, 0.05, p["yellow"])
    # Incomer section: digital power meter
    box(-0.69, -0.258, 1.28, 0.2, 0.008, 0.1, mat("ScreenTeal", (0.2, 0.75, 0.9), emit=2.5))
    # Bus duct trunk and cable entry on the roof
    box(0, 0.22, 2.1, 1.84, 0.2, 0.16, p["steel"], bevel=0.01)
    for x in (-0.6, 0.0, 0.6):
        box(x, 0.22, 2.19, 0.14, 0.24, 0.02, p["grille"])
    box(0, -0.18, 2.005, 1.8, 0.04, 0.01, p["yellow"])
    export("swgear_big")

def make_gen_big():
    """Gas turbine generator package, 2x2 tiles."""
    reset(); p = P()
    paint = mat("TurbineOrange", (0.88, 0.58, 0.14), rough=0.45)
    concrete = mat("Concrete", (0.55, 0.55, 0.53), rough=0.95)
    tank = mat("FuelTank", (0.82, 0.83, 0.82), rough=0.5)
    exhaust = mat("Exhaust", (0.22, 0.2, 0.19), rough=0.6, metal=0.5)
    box(0, 0, 0.04, 1.92, 1.92, 0.08, concrete)
    box(0, -0.36, 0.12, 1.82, 1.02, 0.08, p["black"])
    box(0, -0.36, 0.78, 1.76, 0.96, 1.24, paint, bevel=0.025)
    for i, x in enumerate((-0.6, -0.15, 0.3)):
        box(x, -0.845, 0.72, 0.38, 0.02, 0.96, mat("TurbineDoor", (0.92, 0.63, 0.17), rough=0.4), bevel=0.006)
        box(x + 0.14, -0.86, 0.75, 0.02, 0.025, 0.14, p["steel"])
        for k in range(5):
            box(x, -0.858, 0.32 + k * 0.05, 0.28, 0.008, 0.022, p["grille"])
    box(0.68, -0.855, 1.1, 0.22, 0.01, 0.14, p["dark"])
    box(0.68, -0.86, 1.1, 0.16, 0.005, 0.08, p["green"])
    box(0, -0.86, 1.36, 1.74, 0.01, 0.05, p["yellow"])
    # Air-intake filter house with louvers
    box(-0.42, -0.36, 1.68, 0.78, 0.86, 0.56, paint, bevel=0.015)
    for k in range(7):
        box(-0.42, -0.795, 1.47 + k * 0.065, 0.7, 0.03, 0.03, p["grille"], rot=(math.radians(-30), 0, 0))
    # Exhaust stack
    box(0.55, -0.36, 1.48, 0.42, 0.42, 0.16, exhaust, bevel=0.01)
    cyl(0.55, -0.36, 2.1, 0.15, 1.1, exhaust, verts=16)
    cyl(0.55, -0.36, 2.66, 0.17, 0.05, p["black"], verts=16)
    cyl(0.55, -0.36, 2.62, 0.158, 0.06, mat("ExhaustHot", (0.12, 0.1, 0.09), rough=0.8), verts=16)
    # Fuel tank on saddles and a radiator cooler at the back
    cyl(-0.3, 0.55, 0.42, 0.24, 1.2, tank, verts=20, rot=(0, math.pi / 2, 0))
    for x in (-0.9, 0.3):
        sphere(x, 0.55, 0.42, 0.24, tank, seg=14)
    for x in (-0.65, 0.05):
        box(x, 0.55, 0.16, 0.1, 0.4, 0.2, p["steel"])
    rod((-0.3, 0.55, 0.66), (-0.3, 0.12, 0.66), 0.02, p["steel"])
    box(0.72, 0.52, 0.5, 0.4, 0.66, 0.84, mat("Radiator", (0.3, 0.32, 0.34), rough=0.5), bevel=0.01)
    for y in (0.36, 0.68):
        cyl(0.72, y, 0.93, 0.13, 0.02, p["grille"], verts=20)
        for a in range(3):
            box(0.72, y, 0.945, 0.22, 0.04, 0.008, p["steel"], rot=(0, 0, a * math.pi / 3))
    box(0.72, 0.185, 0.5, 0.3, 0.01, 0.6, p["grille"])
    export("gen_big")

def make_solar_big():
    """Bifacial solar farm block, 3x3 tiles."""
    reset(); p = P()
    cell = mat("SolarCell", (0.1, 0.17, 0.4), rough=0.12, metal=0.4)
    frame = mat("PanelFrame", (0.78, 0.8, 0.82), rough=0.3, metal=0.6)
    tilt = math.radians(-25)
    for row in (-0.98, 0.0, 0.98):
        for x in (-1.2, 0.0, 1.2):
            cyl(x, row + 0.18, 0.4, 0.03, 0.8, p["steel"], verts=8)
            cyl(x, row - 0.18, 0.24, 0.03, 0.48, p["steel"], verts=8)
            box(x, row + 0.0, 0.05, 0.12, 0.5, 0.1, mat("Concrete", (0.55, 0.55, 0.53), rough=0.95))
        rod((-1.35, row, 0.58), (1.35, row, 0.58), 0.025, p["steel"])
        box(0, row, 0.66, 2.84, 0.84, 0.035, frame, rot=(tilt, 0, 0))
        box(0, row - 0.003, 0.68, 2.8, 0.8, 0.02, cell, rot=(tilt, 0, 0))
        for i in range(1, 8):
            box(-1.4 + i * 0.35, row - 0.004, 0.692, 0.01, 0.79, 0.006, frame, rot=(tilt, 0, 0))
        box(0, row - 0.004, 0.692, 2.79, 0.01, 0.006, frame, rot=(tilt, 0, 0))
    # String inverter and combiner box
    box(1.32, 1.42, 0.35, 0.22, 0.12, 0.34, p["white"], bevel=0.01)
    box(1.32, 1.355, 0.42, 0.1, 0.005, 0.05, p["screen"])
    box(1.32, 1.43, 0.09, 0.06, 0.06, 0.18, p["steel"])
    export("solar_big")

def make_bess():
    """Battery energy storage cabinet row, 2x1 tiles."""
    reset(); p = P()
    shell = mat("BessShell", (0.88, 0.9, 0.9), rough=0.45)
    door = mat("BessDoor", (0.2, 0.45, 0.35), rough=0.4)
    box(0, 0, 0.05, 1.84, 0.84, 0.1, p["dark"])
    box(0, 0, 0.84, 1.8, 0.8, 1.48, shell, bevel=0.02)
    for i in range(6):
        x = -0.75 + i * 0.3
        box(x, -0.405, 0.8, 0.27, 0.02, 1.3, door, bevel=0.006)
        for k in range(4):
            box(x, -0.418, 0.24 + k * 0.05, 0.2, 0.008, 0.022, p["grille"])
        box(x, -0.418, 1.3, 0.16, 0.008, 0.02, p["green"])
        box(x + 0.1, -0.422, 0.85, 0.018, 0.02, 0.14, p["steel"])
    box(-0.45, -0.418, 1.0, 0.09, 0.006, 0.09, p["yellow"], rot=(0, math.radians(45), 0))
    box(-0.45, -0.422, 1.0, 0.012, 0.004, 0.05, p["black"])
    # Roof: standing-seam ribs and vent caps
    for i in range(9):
        box(-0.8 + i * 0.2, 0, 1.6, 0.02, 0.78, 0.03, shell)
    for x in (-0.5, 0.5):
        box(x, 0.1, 1.66, 0.22, 0.22, 0.08, p["grille"], bevel=0.01)
        box(x, 0.1, 1.72, 0.28, 0.28, 0.02, shell)
    # End-wall HVAC unit
    box(0.95, 0, 0.9, 0.1, 0.6, 0.7, mat("HvacGray", (0.7, 0.72, 0.74), rough=0.5), bevel=0.01)
    cyl(1.0, 0, 0.95, 0.2, 0.02, p["grille"], verts=20, rot=(0, math.pi / 2, 0))
    for a in range(3):
        box(1.012, 0, 0.95, 0.006, 0.3, 0.04, p["steel"], rot=(a * math.pi / 3, 0, 0))
    export("bess")

def make_elevator():
    """Elevator and cable riser shaft, 1x1 tile."""
    reset(); p = P()
    frame = mat("ShaftFrame", (0.45, 0.48, 0.52), rough=0.4, metal=0.5)
    glass = mat("ShaftGlass", (0.32, 0.45, 0.55), rough=0.08, metal=0.6)
    doors = mat("BrushedSteel", (0.75, 0.77, 0.8), rough=0.25, metal=0.8)
    riser = mat("RiserYellow", (1.0, 0.75, 0.2), rough=0.5)
    H = 2.6
    box(0, 0, 0.03, 0.92, 0.92, 0.06, p["dark"])
    for x in (-0.42, 0.42):
        for y in (-0.42, 0.42):
            box(x, y, H / 2, 0.08, 0.08, H, frame, bevel=0.005)
    box(0, 0, H - 0.02, 0.92, 0.92, 0.1, frame, bevel=0.01)
    for (x, y, sx, sy) in ((0.42, 0, 0.025, 0.78), (-0.42, 0, 0.025, 0.78), (0, 0.42, 0.78, 0.025)):
        box(x, y, H / 2, sx, sy, H - 0.12, glass)
        for z in (0.9, 1.8):
            box(x, y, z, sx + 0.02, sy + 0.02, 0.04, frame)
    # Front: door portal, sliding doors, floor indicator and call panel
    box(0, -0.43, H - 0.27, 0.78, 0.04, 0.36, frame)
    box(-0.39, -0.43, 1.06, 0.06, 0.04, 2.12, frame)
    box(0.39, -0.43, 1.06, 0.06, 0.04, 2.12, frame)
    box(-0.165, -0.425, 1.07, 0.33, 0.025, 2.06, doors, bevel=0.003)
    box(0.165, -0.425, 1.07, 0.33, 0.025, 2.06, doors, bevel=0.003)
    box(0, -0.452, 2.22, 0.24, 0.01, 0.1, p["black"])
    box(0, -0.458, 2.22, 0.16, 0.004, 0.06, p["amber"])
    box(0.39, -0.455, 1.15, 0.05, 0.01, 0.16, p["dark"])
    cyl(0.39, -0.462, 1.19, 0.014, 0.008, p["amber"], verts=10, rot=(math.pi / 2, 0, 0))
    cyl(0.39, -0.462, 1.11, 0.014, 0.008, p["white"], verts=10, rot=(math.pi / 2, 0, 0))
    # Cable riser bundle in the back-left corner, with trays branching off
    box(-0.3, 0.3, H / 2, 0.16, 0.16, H - 0.1, mat("RiserTray", (0.35, 0.37, 0.4), rough=0.5, metal=0.4))
    for i, (dx, dy) in enumerate(((-0.04, -0.04), (0.04, -0.04), (-0.04, 0.04), (0.04, 0.04))):
        cyl(-0.3 + dx, 0.3 + dy, H / 2, 0.03, H - 0.1, riser if i % 2 == 0 else mat("FiberOrange", (1.0, 0.55, 0.12), rough=0.5), verts=8)
    # Machine room cap on top
    box(0.1, 0.1, H + 0.12, 0.5, 0.5, 0.18, frame, bevel=0.01)
    for k in range(4):
        box(0.1, -0.155, H + 0.08 + k * 0.035, 0.4, 0.01, 0.015, p["grille"])
    export("elevator")

def make_ups_li():
    """Modular lithium-ion UPS, 1x1 tile."""
    reset(); p = P()
    body = mat("LiUpsBody", (0.12, 0.14, 0.16), rough=0.35, metal=0.2)
    panel = mat("LiUpsPanel", (0.17, 0.19, 0.22), rough=0.3, metal=0.2)
    teal = mat("LedTeal", (0.15, 0.85, 0.8), emit=4)
    box(0, 0, 0.04, 0.9, 0.76, 0.08, p["black"])
    box(0, 0, 0.98, 0.92, 0.76, 1.8, body, bevel=0.015)
    for i, x in enumerate((-0.3, 0.0, 0.3)):
        box(x, -0.385, 0.98, 0.28, 0.02, 1.7, panel, bevel=0.005)
        box(x - 0.13, -0.397, 0.98, 0.008, 0.006, 1.6, teal)
        if i == 1:
            box(x, -0.4, 1.45, 0.22, 0.01, 0.3, p["black"], bevel=0.004)
            box(x, -0.406, 1.45, 0.18, 0.004, 0.25, mat("ScreenTeal", (0.2, 0.75, 0.9), emit=2.5))
            for k in range(5):
                box(x - 0.06 + k * 0.03, -0.4, 1.2, 0.02, 0.006, 0.02, p["green"])
        for r in range(9 if i != 1 else 5):
            for c in range(5):
                if i == 1 and r > 4: continue
                box(x - 0.08 + c * 0.04, -0.398, 0.25 + r * 0.06 + (0 if c % 2 else 0.03), 0.022, 0.006, 0.022, p["grille"])
    box(0, 0, 1.885, 0.84, 0.66, 0.01, mat("UpsTop", (0.2, 0.75, 0.35), rough=0.5))
    box(0, 0.25, 1.92, 0.5, 0.14, 0.06, p["grille"])
    export("ups_li")

def make_rack(name, units, style):
    """Rack cabinet shell. Devices, blanking and the roof gauge are drawn by the game on top of this.
    Height matches Models.RackHeight: units * 0.042 + 0.12."""
    reset(); p = P()
    H = units * 0.042 + 0.12
    body = mat("RackBody", (0.11, 0.12, 0.14), rough=0.45, metal=0.3)
    blank = mat("RackBlank", (0.05, 0.05, 0.06), rough=0.6)
    rail = mat("RackRail", (0.32, 0.34, 0.38), rough=0.35, metal=0.7)
    accent = {"std": None, "hd": mat("RackAccentBlue", (0.2, 0.5, 0.95), rough=0.4),
              "ai": mat("RackAccentTeal", (0.15, 0.85, 0.65), rough=0.4)}[style]
    # Levelling feet + plinth
    for x in (-0.27, 0.27):
        for y in (-0.4, 0.4):
            cyl(x, y, 0.015, 0.025, 0.03, p["rubber"], verts=8)
    box(0, 0, 0.045, 0.62, 0.9, 0.04, p["black"])
    # Side panels, roof, back door
    for x in (-0.31, 0.31):
        box(x, 0, H / 2 + 0.03, 0.02, 0.92, H - 0.06, body, bevel=0.004)
        if style != "std":
            for k in range(int((H - 0.4) / 0.12)):
                box(x * 1.035, 0.15, 0.3 + k * 0.12, 0.004, 0.4, 0.03, blank)
    box(0, 0, H - 0.015, 0.64, 0.92, 0.03, body, bevel=0.004)
    box(0, 0.45, H / 2 + 0.03, 0.6, 0.02, H - 0.1, body)
    for k in range(int((H - 0.3) / 0.05)):
        box(0, 0.462, 0.2 + k * 0.05, 0.48, 0.004, 0.018, blank)
    box(0.25, 0.465, H * 0.55, 0.02, 0.02, 0.18, p["steel"])
    # Interior: blanking behind the device slots, vertical mounting rails at the front
    box(0, -0.43, H / 2 + 0.02, 0.58, 0.02, H - 0.1, blank)
    for x in (-0.285, 0.285):
        box(x, -0.47, H / 2 + 0.02, 0.03, 0.03, H - 0.08, rail)
        for k in range(units // 3):
            box(x, -0.486, 0.1 + k * 0.126, 0.012, 0.002, 0.004, p["white"])
    box(0, -0.47, H - 0.02, 0.6, 0.035, 0.04, body)
    box(0, -0.47, 0.07, 0.6, 0.035, 0.04, body)
    box(-0.18, -0.489, H - 0.02, 0.12, 0.002, 0.022, p["white"])
    if accent is not None:
        for x in (-0.315, 0.315):
            box(x, -0.461, H / 2 + 0.03, 0.026, 0.006, H - 0.1, accent)
    if style == "ai":
        # Reinforced corner posts and a rear-door heat exchanger with coolant lines
        for x in (-0.3, 0.3):
            for y in (-0.44, 0.44):
                box(x, y, H / 2 + 0.03, 0.045, 0.045, H - 0.06, rail)
        box(0, 0.49, H / 2 + 0.06, 0.56, 0.06, H - 0.2, mat("RdhxCoil", (0.25, 0.28, 0.32), rough=0.4, metal=0.5), bevel=0.006)
        for k in range(int((H - 0.3) / 0.04)):
            box(0, 0.522, 0.2 + k * 0.04, 0.5, 0.004, 0.012, blank)
        sup = mat("PipeSupply", (0.18, 0.4, 0.95), rough=0.35, metal=0.3)
        ret = mat("PipeReturn", (0.92, 0.25, 0.18), rough=0.35, metal=0.3)
        for x, m in ((-0.2, sup), (-0.1, ret)):
            rod((x, 0.53, 0.12), (x, 0.53, H - 0.1), 0.02, m)
            rod((x, 0.53, 0.12), (x, 0.46, 0.04), 0.02, m)
        box(0.18, 0.525, H - 0.2, 0.1, 0.01, 0.05, p["green"])
    export(name)

# ------------------------------------------------------------------ scenery
def make_tree_pine():
    reset()
    bark = mat("Bark", (0.38, 0.26, 0.16), rough=0.9)
    leaf = mat("Leaf", (0.18, 0.42, 0.18), rough=0.8)
    leaf2 = mat("LeafLight", (0.22, 0.5, 0.2), rough=0.8)
    cone(0, 0, 0, 0.08, 0.6, bark, verts=6, r2=0.05)
    cone(0, 0, 0.3, 0.46, 0.62, leaf, verts=8, jitter=0.12, seed=1)
    cone(0, 0, 0.66, 0.36, 0.55, leaf2, verts=8, jitter=0.12, seed=2)
    cone(0, 0, 0.98, 0.24, 0.5, leaf, verts=7, jitter=0.1, seed=3)
    export("tree_pine")

def make_tree_round():
    reset()
    bark = mat("Bark", (0.38, 0.26, 0.16), rough=0.9)
    leaf = mat("Leaf", (0.2, 0.44, 0.16), rough=0.8)
    leaf2 = mat("LeafLight", (0.26, 0.52, 0.18), rough=0.8)
    cone(0, 0, 0, 0.08, 0.62, bark, verts=6, r2=0.05)
    rod((0, 0, 0.42), (0.16, 0.04, 0.66), 0.03, bark, verts=5)
    rod((0, 0, 0.48), (-0.14, -0.06, 0.7), 0.03, bark, verts=5)
    blob(0, 0, 0.82, 0.38, 0.36, 0.3, leaf, jitter=0.15, seed=4)
    blob(0.17, 0.06, 0.72, 0.24, 0.24, 0.2, leaf2, jitter=0.15, seed=5)
    blob(-0.15, -0.08, 0.76, 0.24, 0.22, 0.2, leaf2, jitter=0.15, seed=6)
    blob(0.02, -0.02, 1.02, 0.24, 0.24, 0.18, leaf, jitter=0.15, seed=7)
    export("tree_round")

def make_rock(name, variant):
    reset()
    stone = mat("Stone", (0.47, 0.47, 0.49), rough=0.9)
    stone2 = mat("StoneDark", (0.38, 0.38, 0.4), rough=0.9)
    moss = mat("Moss", (0.3, 0.42, 0.2), rough=1.0)
    if variant == 1:
        blob(0, 0, 0.1, 0.32, 0.26, 0.2, stone, jitter=0.22, seed=11)
        blob(0.03, -0.02, 0.22, 0.18, 0.16, 0.08, moss, jitter=0.2, seed=12)
    else:
        blob(-0.1, 0.02, 0.1, 0.24, 0.2, 0.17, stone, jitter=0.22, seed=13)
        blob(0.17, -0.06, 0.06, 0.15, 0.13, 0.1, stone2, jitter=0.25, seed=14)
        blob(0.04, 0.17, 0.04, 0.1, 0.09, 0.07, stone2, jitter=0.25, seed=15)
    export(name)

def make_evap():
    """Evaporative cooler, 1x1 tile: wetted media on three sides, a big fan on top."""
    reset(); p = P()
    body = mat("EvapBody", (0.55, 0.8, 0.85), rough=0.45)
    media = mat("EvapMedia", (0.3, 0.55, 0.5), rough=0.95)
    water = mat("WaterBlue", (0.2, 0.5, 0.9), emit=1.5)
    box(0, 0, 0.08, 0.92, 0.88, 0.16, p["grille"])
    box(0, 0, 0.94, 0.86, 0.82, 1.56, body, bevel=0.02)
    for i in range(5):
        z = 0.35 + i * 0.27
        box(0, -0.415, z, 0.74, 0.02, 0.21, media)
        box(-0.435, 0, z, 0.02, 0.7, 0.21, media)
        box(0.435, 0, z, 0.02, 0.7, 0.21, media)
    box(0, 0, 1.74, 0.8, 0.78, 0.04, body)
    cyl(0, 0, 1.79, 0.34, 0.06, p["grille"], verts=28, bevel=0.005)
    cyl(0, 0, 1.83, 0.06, 0.04, p["steel"], verts=12)
    for a in range(4):
        box(0, 0, 1.83, 0.56, 0.07, 0.012, p["steel"], rot=(0, 0, a * math.pi / 4))
    # Water feed pipe and level indicator
    rod((0.3, 0.42, 0.05), (0.3, 0.42, 1.5), 0.03, mat("Pipe", (0.7, 0.72, 0.75), rough=0.3, metal=0.7))
    box(0.3, -0.43, 0.22, 0.12, 0.012, 0.05, water)
    export("evap")

def make_heatx():
    """District heat exchanger skid, 2x1 tiles: plate exchanger, hot/return loops, pump."""
    reset(); p = P()
    frame = mat("PlateFrame", (0.75, 0.76, 0.78), rough=0.35, metal=0.5)
    plates = mat("Plates", (0.55, 0.56, 0.6), rough=0.4, metal=0.6)
    hot = mat("PipeHot", (0.85, 0.3, 0.2), rough=0.4)
    cold = mat("PipeCold", (0.25, 0.45, 0.9), rough=0.4)
    pump = mat("PumpGreen", (0.2, 0.35, 0.25), rough=0.5)
    box(0, 0, 0.06, 1.84, 0.85, 0.12, mat("Concrete", (0.5, 0.5, 0.5), rough=0.9))
    box(-0.35, 0, 0.75, 0.7, 0.5, 1.2, frame, bevel=0.02)
    for i in range(9):
        box(-0.66 + i * 0.08, -0.26, 0.75, 0.03, 0.02, 1.05, plates)
    cyl(0.35, -0.18, 0.67, 0.12, 1.1, hot, verts=16)
    cyl(0.35, 0.18, 0.67, 0.12, 1.1, cold, verts=16)
    rod((0.35, -0.18, 1.12), (-0.05, -0.18, 1.12), 0.05, hot)
    rod((0.35, 0.18, 1.0), (-0.05, 0.18, 1.0), 0.05, cold)
    box(0.7, 0, 0.35, 0.24, 0.5, 0.3, pump, bevel=0.02)
    cyl(0.7, 0, 0.55, 0.1, 0.1, p["steel"], verts=12)
    box(0.7, -0.26, 0.55, 0.06, 0.01, 0.06, p["amber"])
    for y, m in ((-0.18, hot), (0.18, cold)):
        rod((0.6, y, 0.12), (0.9, y, 0.12), 0.06, m)
        rod((0.9, y, 0.12), (0.9, y, -0.1), 0.06, m)
    export("heatx")

def make_access():
    """Access control: two speed-gate pedestals with glass flaps and a CCTV pole, 1x1 tile."""
    reset(); p = P()
    glass = mat("GateGlass", (0.5, 0.7, 0.8), rough=0.05, metal=0.3)
    for x in (-0.36, 0.36):
        box(x, 0, 0.5, 0.16, 0.75, 1.0, p["steel"], bevel=0.02)
        box(x, -0.25, 1.01, 0.1, 0.12, 0.02, p["green"])
        box(x, 0.2, 1.01, 0.08, 0.08, 0.02, p["dark"])
    box(-0.18, 0, 0.7, 0.2, 0.02, 0.5, glass)
    box(0.18, 0, 0.7, 0.2, 0.02, 0.5, glass)
    box(0.42, 0.36, 1.3, 0.05, 0.05, 2.6, mat("PoleGray", (0.3, 0.32, 0.35), rough=0.5))
    box(0.36, 0.3, 2.55, 0.12, 0.22, 0.1, p["white"], bevel=0.01)
    box(0.36, 0.18, 2.55, 0.05, 0.01, 0.05, p["red"])
    export("access")

def make_dock():
    """Loading dock, 2x2 tiles: raised platform against the building (+Y), apron at the front."""
    reset(); p = P()
    concrete = mat("Concrete", (0.62, 0.62, 0.6), rough=0.9)
    asphalt = mat("Asphalt", (0.3, 0.3, 0.32), rough=0.95)
    door = mat("RollerDoor", (0.5, 0.55, 0.6), rough=0.5, metal=0.4)
    box(0, 0.48, 0.3, 1.92, 0.96, 0.6, concrete, bevel=0.01)
    box(0, -0.48, 0.006, 1.92, 0.96, 0.012, asphalt)
    for i in range(4):
        box(-0.77 + i * 0.52, -0.02, 0.61, 0.24, 0.06, 0.02, p["yellow"])
    for x in (-0.45, 0.45):
        box(x, -0.03, 0.35, 0.14, 0.08, 0.3, p["rubber"], bevel=0.01)
    box(0, 0.15, 0.61, 0.8, 0.3, 0.02, p["steel"])
    box(0, 0.9, 1.45, 1.2, 0.06, 1.7, door)
    for i in range(8):
        box(0, 0.86, 0.7 + i * 0.2, 1.1, 0.01, 0.02, p["grille"])
    box(0, 0.6, 2.35, 1.5, 0.5, 0.06, p["grille"])
    box(0.7, 0.86, 1.4, 0.05, 0.01, 0.05, p["green"])
    export("dock")

def make_soundwall():
    """Acoustic barrier panel between two posts, 1x1 tile."""
    reset(); p = P()
    panel = mat("BarrierPanel", (0.45, 0.5, 0.42), rough=0.9)
    rib = mat("BarrierRib", (0.36, 0.4, 0.34), rough=0.9)
    box(0, 0, 1.2, 0.98, 0.12, 2.4, panel)
    for i in range(6):
        box(0, -0.065, 0.2 + i * 0.4, 0.9, 0.01, 0.03, rib)
    for x in (-0.47, 0.47):
        box(x, 0, 1.25, 0.06, 0.16, 2.5, p["steel"])
    export("soundwall")

BUILDS = [
    ("crac", lambda: make_crac("crac", 1.8, (0.85, 0.87, 0.88))),
    ("inrow", lambda: make_crac("inrow", 2.0, (0.75, 0.85, 0.95))),
    ("core", make_core), ("isp", make_isp), ("xfmr", make_xfmr), ("gen", make_gen), ("ups", make_ups),
    ("swgear", make_swgear), ("desk", make_desk), ("solar", make_solar), ("suppress", make_suppress),
    ("person", make_person),
    ("liquid", make_liquid), ("xfmr_big", make_xfmr_big), ("swgear_big", make_swgear_big),
    ("gen_big", make_gen_big), ("solar_big", make_solar_big), ("bess", make_bess),
    ("elevator", make_elevator), ("ups_li", make_ups_li),
    ("evap", make_evap), ("heatx", make_heatx), ("access", make_access), ("dock", make_dock), ("soundwall", make_soundwall),
    ("rack_std", lambda: make_rack("rack_std", 42, "std")),
    ("rack_hd", lambda: make_rack("rack_hd", 48, "hd")),
    ("rack_ai", lambda: make_rack("rack_ai", 52, "ai")),
    ("tree_pine", make_tree_pine), ("tree_round", make_tree_round),
    ("rock", lambda: make_rock("rock", 1)), ("rock_b", lambda: make_rock("rock_b", 2)),
]
for _name, _fn in BUILDS:
    if not ONLY or _name in ONLY:
        _fn()
print("ALL MODELS DONE")
