"""Turns a downloaded car .glb into the compact model the pet renders.

Blender -b --python tools/model/prepare_car.py -- <source.glb> <out.glb> [texture size]

Output nodes (glTF: +Y up, +Z forward, model units, origin between the axles at axle height):
  body    opaque shell, materials "body" and "paint" (paint gets clear coat at runtime)
  glass   windows and lamp lenses, material "glass"
  wheel_FL wheel_FR wheel_RL wheel_RR   one mesh per wheel, node origin = wheel centre
All materials share one baked atlas: base colour plus a glTF metallic-roughness map.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
TEX = int(args[2]) if len(args) > 2 else 2048
TARGET_TRIS = 60000

# Model-specific clean-up for the 1975 Porsche 911 Turbo (Sketchfab): ground AO plane and radio antenna.
DROP_OBJECTS = {"Object_140", "Object_49"}
DROP_MATERIALS = ("coat", "stickers", "Material.001")   # clear-coat shell duplicate, alpha decals, ground
GLASS_MATERIALS = ("glass", "lights_refraction")
PAINT_MATERIALS = ("paint",)
# Metallic and roughness per material, by name fragment. The downloaded maps make nearly everything mirror-like
# (tyres at roughness ~0.1), which reads as chrome at pet size, so these values replace them.
SURFACE = {"tire": (0, .88), "rim": (1, .3), "chromes": (.8, .32), "plastics": (0, .62), "black": (0, .7),
           "lights": (0, .12), "plate": (.3, .4), "wunderbaum": (0, .85), "paint": (.6, .3)}
DEFAULT_SURFACE = (0, .6)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
scene = bpy.context.scene
for o in [o for o in scene.objects if o.type == "MESH"]:
    mw = o.matrix_world.copy(); o.parent = None; o.matrix_world = mw
for o in [o for o in scene.objects if o.type != "MESH"]:
    bpy.data.objects.remove(o)

def mat_name(o):
    return o.data.materials[0].name.lower() if o.data.materials and o.data.materials[0] else ""
for o in list(scene.objects):
    if o.name in DROP_OBJECTS or any(k.lower() in mat_name(o) for k in DROP_MATERIALS):
        bpy.data.objects.remove(o)
meshes = [o for o in scene.objects if o.type == "MESH"]
print(f"[prep] {len(meshes)} meshes after clean-up")

def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)])

# Wheels: tyres define the centres; parts fully inside a tyre's circle belong to that wheel.
tires = [o for o in meshes if "tire" in mat_name(o)]
assert len(tires) == 4, f"expected 4 tyres, found {len(tires)}"
wheels = []
for t in tires:
    lo, hi = bounds([t]); wheels.append(((lo + hi) / 2, (hi.z - lo.z) / 2))
def wheel_of(o):
    lo, hi = bounds([o]); c = (lo + hi) / 2
    for i, (wc, r) in enumerate(wheels):
        if abs(c.x - wc.x) < .35 and Vector((0, c.y - wc.y, c.z - wc.z)).length < r * .9 \
                and hi.y - lo.y < 2.1 * r and hi.z - lo.z < 2.1 * r:
            return i
    return None

ys = sorted(wc.y for wc, _ in wheels)
pivot = Vector((0, (ys[0] + ys[-1]) / 2, sum(wc.z for wc, _ in wheels) / 4))
for o in meshes:
    o.matrix_world = Matrix.Translation(-pivot) @ o.matrix_world
bpy.context.view_layer.update()
wheels = [(wc - pivot, r) for wc, r in wheels]

# Decimate the heavy meshes until the whole car is near TARGET_TRIS.
def tris(o):
    o.data.calc_loop_triangles(); return len(o.data.loop_triangles)
total = sum(tris(o) for o in meshes)
ratio = min(1, TARGET_TRIS / total)
for o in meshes:
    if tris(o) > 1500 and ratio < 1:
        m = o.modifiers.new("decimate", "DECIMATE"); m.ratio = ratio; m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=m.name)
print(f"[prep] triangles {total} -> {sum(tris(o) for o in meshes)}")

# ---------------------------------------------------------------- bake one atlas for everything
for o in meshes:
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    o.data.uv_layers.active_index = 0
    for uv in o.data.uv_layers: uv.active_render = uv.name == o.data.uv_layers[0].name
    atlas = o.data.uv_layers.new(name="atlas")
    o.data.uv_layers.active = atlas
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.004, scale_to_bounds=False)
bpy.ops.uv.pack_islands(margin=.004, rotate=True)
bpy.ops.object.mode_set(mode="OBJECT")

albedo = bpy.data.images.new("car_albedo", TEX, TEX, alpha=False)
metal_img = bpy.data.images.new("bake_metal", TEX // 2, TEX // 2, alpha=False, float_buffer=False)
rough_img = bpy.data.images.new("bake_rough", TEX // 2, TEX // 2, alpha=False, float_buffer=False)
for img in (metal_img, rough_img): img.colorspace_settings.name = "Non-Color"

scene.render.engine = "CYCLES"
scene.cycles.samples = 4
scene.cycles.device = "CPU"
scene.render.bake.margin = 6
materials = {m for o in meshes for m in o.data.materials if m}

def target_node(m, image):
    nt = m.node_tree
    n = nt.nodes.get("bake_target") or nt.nodes.new("ShaderNodeTexImage")
    n.name = "bake_target"; n.image = image
    for x in nt.nodes: x.select = False
    n.select = True; nt.nodes.active = n

def bake(kind, image, socket):
    saved = []
    for m in materials:
        target_node(m, image)
        if socket:
            nt = m.node_tree
            bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
            out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
            em = nt.nodes.new("ShaderNodeEmission"); em.name = "bake_emit"
            src = bsdf.inputs[socket] if bsdf else None
            if socket in ("Metallic", "Roughness"):
                fixed = next((v for k, v in SURFACE.items() if k in m.name.lower()), DEFAULT_SURFACE)
                v = fixed[0] if socket == "Metallic" else fixed[1]
                em.inputs["Color"].default_value = (v, v, v, 1)
            elif src is not None and src.links:
                nt.links.new(src.links[0].from_socket, em.inputs["Color"])
            else:
                v = src.default_value if src is not None else (1.0 if socket == "Roughness" else 0.0)
                em.inputs["Color"].default_value = tuple(v) if hasattr(v, "__len__") else (v, v, v, 1)
            old = out.inputs["Surface"].links[0].from_socket if out.inputs["Surface"].links else None
            saved.append((nt, out, old, em))
            nt.links.new(em.outputs[0], out.inputs["Surface"])
    bpy.ops.object.bake(type="EMIT", use_clear=True)
    for nt, out, old, em in saved:
        if old: nt.links.new(old, out.inputs["Surface"])
        nt.nodes.remove(em)

# Base colour through emission: a diffuse-colour bake is black for metallic surfaces.
bake("EMIT", albedo, "Base Color")
bake("EMIT", metal_img, "Metallic")
bake("EMIT", rough_img, "Roughness")
print("[prep] baked")

# glTF metallic-roughness map: G = roughness, B = metallic.
orm = bpy.data.images.new("car_orm", TEX // 2, TEX // 2, alpha=False)
orm.colorspace_settings.name = "Non-Color"
mp, rp = list(metal_img.pixels), list(rough_img.pixels)
px = [0.0] * len(mp)
for i in range(0, len(mp), 4):
    px[i], px[i + 1], px[i + 2], px[i + 3] = 1.0, rp[i], mp[i], 1.0
orm.pixels = px

# ---------------------------------------------------------------- simple glTF materials
def gltf_material(name, image=None, color=(1, 1, 1, 1), blend=False, rough=.1, metal=0.0):
    m = bpy.data.materials.new(name); nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = rough; bsdf.inputs["Metallic"].default_value = metal
    if image:
        tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = albedo
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        ormn = nt.nodes.new("ShaderNodeTexImage"); ormn.image = orm
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(ormn.outputs["Color"], sep.inputs[0])
        nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
        nt.links.new(sep.outputs["Blue"], bsdf.inputs["Metallic"])
    if blend:
        bsdf.inputs["Alpha"].default_value = color[3]
        m.surface_render_method = "BLENDED"
    return m

mat_body = gltf_material("body", image=True)
mat_paint = gltf_material("paint", image=True)
mat_glass = gltf_material("glass", color=(.02, .025, .03, .35), blend=True, rough=.05)

def assign(objs, mat):
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(mat)
        o.data.uv_layers.active = o.data.uv_layers["atlas"]
        for uv in [uv for uv in o.data.uv_layers if uv.name != "atlas"]:
            o.data.uv_layers.remove(uv)

groups = {"body": [], "glass": [], "wheel_FL": [], "wheel_FR": [], "wheel_RL": [], "wheel_RR": []}
paint = []
for o in meshes:
    name = mat_name(o)
    w = wheel_of(o)
    if w is not None:
        wc = wheels[w][0]
        key = "wheel_" + ("F" if wc.y < 0 else "R") + ("L" if wc.x > 0 else "R")  # car faces -Y; its left is +X
        groups[key].append(o)
    elif any(k in name for k in GLASS_MATERIALS):
        groups["glass"].append(o)
    else:
        groups["body"].append(o)
        if any(k in name for k in PAINT_MATERIALS): paint.append(o)
for k, v in groups.items(): print(f"[prep] {k}: {len(v)} parts")
assign([o for o in groups["body"] if o not in paint], mat_body)
assign(paint, mat_paint)
assign(groups["glass"], mat_glass)
for k in groups:
    if k.startswith("wheel"): assign(groups[k], mat_body)

def join(name, objs, origin):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active; o.name = name; o.data.name = name
    # Bake the transform into the mesh, then move the origin to `origin`.
    o.data.transform(o.matrix_world); o.matrix_world = Matrix.Identity(4)
    o.data.transform(Matrix.Translation(-origin)); o.location = origin
    for p in o.data.polygons: p.use_smooth = True
    return o

result = []
for key, objs in groups.items():
    assert objs, f"no parts for {key}"
    if key.startswith("wheel"):
        idx = next(i for i, (wc, _) in enumerate(wheels) if ("F" if wc.y < 0 else "R") == key[6] and ("L" if wc.x > 0 else "R") == key[7])
        result.append(join(key, objs, wheels[idx][0]))
    else:
        result.append(join(key, objs, Vector((0, 0, 0))))
for o in result:
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
for img, fmt in ((albedo, "JPEG"), (orm, "PNG")):
    img.file_format = fmt

bpy.ops.object.select_all(action="SELECT")
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_apply=True,
                          export_image_format="AUTO", export_jpeg_quality=88, export_texcoords=True,
                          export_normals=True, export_materials="EXPORT", export_yup=True,
                          export_animations=False, export_extras=False)
# Imported objects can carry stale bounding boxes, so measure the final meshes from their vertices.
radius = max(abs(v.co.z) for o in result if o.name.startswith("wheel") for v in o.data.vertices)
print(f"[prep] wheel radius {radius:.4f} wheelbase {ys[-1] - ys[0]:.4f}")
print(f"[prep] wrote {OUT} ({os.path.getsize(OUT) / 1e6:.1f} MB)")
