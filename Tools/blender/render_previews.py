"""
Renders a contact sheet of model previews (one PNG per FBX) for a quick visual check.

  blender -b --factory-startup -P Tools/blender/render_previews.py -- <models_dir> <out_dir> [names...]
"""
import bpy, math, os, sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
NAMES = args[2:] or sorted(f[:-4] for f in os.listdir(SRC) if f.endswith(".fbx"))
os.makedirs(OUT, exist_ok=True)

for name in NAMES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, name + ".fbx"))
    objs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    center = (lo + hi) / 2
    size = max((hi - lo).length, 0.5)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("w"); scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.6, 0.66, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.8

    bpy.ops.mesh.primitive_plane_add(size=size * 6, location=(center.x, center.y, lo.z))
    ground = bpy.context.active_object
    gm = bpy.data.materials.new("ground"); gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.35, 0.37, 0.35, 1)
    ground.data.materials.append(gm)

    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5
    so = bpy.data.objects.new("sun", sun); scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), 0, math.radians(30))

    # Front faces -Y in Blender space after import; look from front-left, above.
    cam = bpy.data.cameras.new("cam"); cam.lens = 50
    co = bpy.data.objects.new("cam", cam); scene.collection.objects.link(co)
    co.location = center + Vector((-0.9, -1.6, 1.1)).normalized() * size * 1.9
    co.rotation_euler = (center - co.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = co
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", name)
