"""Export every rigged piercing (see rig_piercings.py) and the chain link into the mod's runtime models, assets/piercings.npd.

Usage (Blender 5.x, headless):
    blender -b --disable-autoexec "<Piercings_rigged.blend>" --python mods/NipplePiercings/tools/export_model.py -- <out.npd>

Each mesh is evaluated in its rig's rest pose (Subdivision applied) and split by bone: every vertex goes to the bone of its vertex
group with the largest weight (each piece is weighted 1.0 to one bone). Positions are relative to the piercing's attach point
(what goes on the nipple's tip or in the pierced channel), in Blender axes and units (x = the viewer's right seen from the front,
y = back, z = up). The chain link ("link", one bone) is relative to its centre, its long axis along x. No textures: each
material is its base colour, metallic and transmission.

Format (little endian): "NPPD" u32 version=2
  u32 materialCount, per material: str name, f32x4 colour, f32 metallic, f32 transmission
  u32 modelCount, per model: str id, u8 kind (0 on the nipple's tip, 1 through the nipple, 2 chain link),
      f32x3 hook, u32 hookBone,
      u32 boneCount (parents before children, one chain), per bone: str name, i32 parent (-1 root), f32x3 head, f32x3 tail,
          u32 vertexCount, f32x3 positions[], f32x3 normals[],
          u32 submeshCount, per submesh: u32 material, u32 indexCount, u32 indices[]
  str = u8 byte length + UTF-8
"""
import os
import struct
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_path = os.path.abspath(args[0] if args else "piercings.npd")

IDS = {  # object -> model id (the config's names)
    "piercing_stud": "stud",
    "piercing_pendant_crystal": "crystal",
    "piercing_pendant": "heart",
    "piercing_barbell": "barbell",
    "piercing_barbell_curved": "curved",
    "piercing_barbell_curved_small": "curved_small",
    "piercing_ring_nipple": "ring",
    "piercing_ring_small": "ring_small",
    "piercing_loop_big": "hoop",
    "piercing_chain_ring": "chain_ring",
    "piercing_chain_ring_big": "chain_ring_big",
    "piercing_chain_link": "link",
}

materials, mat_index = [], {}


def material(mat):
    if mat.name not in mat_index:
        bsdf = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        tr = bsdf.inputs.get('Transmission Weight') or bsdf.inputs.get('Transmission')
        mat_index[mat.name] = len(materials)
        materials.append((mat.name, tuple(bsdf.inputs['Base Color'].default_value), bsdf.inputs['Metallic'].default_value,
                          tr.default_value if tr else 0.0))
    return mat_index[mat.name]


def export(o, rig):
    for r in [x for x in bpy.data.objects if x.type == 'ARMATURE']:
        r.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    e = o.evaluated_get(dg)
    m = e.to_mesh()
    m.calc_loop_triangles()
    mw = e.matrix_world
    nmat = mw.to_3x3().inverted().transposed()
    if rig is None:  # the chain link: one piece, its own centre
        verts = [mw @ v.co for v in m.vertices]
        lo = Vector([min(p[k] for p in verts) for k in range(3)])
        hi = Vector([max(p[k] for p in verts) for k in range(3)])
        origin = (lo + hi) / 2
        bones = [("link", -1, Vector((lo.x, 0, 0)) - Vector((origin.x, 0, 0)) + Vector((0, 0, 0)), Vector((hi.x - origin.x, 0, 0)))]
        owner = [0] * len(m.vertices)
        kind, hook, hook_bone = 2, Vector(), 0
    else:
        origin = Vector(rig["attach"])
        rb = list(rig.data.bones)
        index = {b.name: i for i, b in enumerate(rb)}
        group_bone = {g.index: index.get(g.name, -1) for g in o.vertex_groups}
        owner = []
        for v in m.vertices:
            best, w = -1, 0.0
            for g in v.groups:
                b = group_bone.get(g.group, -1)
                if b >= 0 and g.weight > w:
                    best, w = b, g.weight
            assert best >= 0, "%s: vertex %d has no bone" % (o.name, v.index)
            owner.append(best)
        bones = [(b.name, index[b.parent.name] if b.parent else -1, rig.matrix_world @ b.head_local - origin,
                  rig.matrix_world @ b.tail_local - origin) for b in rb]
        kind = 0 if rig["kind"] == "tip" else 1
        hook, hook_bone = Vector(rig["hook"]) - origin, int(rig["hook_bone"])
    normals = m.corner_normals
    # Pieces modelled inside out (the heart pendant's chain links: faces and normals pointing in) are turned right way out,
    # or Unity culls their outer faces and draws the dark inside. Judged per bone - do its faces as a whole point away from
    # its centre (area weighted)? Per loose piece the crystal's links (an outer and an inner shell each, the inner one rightly
    # facing in) were flipped wrongly; per bone the outer shell outweighs it [observed 2026-10-25].
    centre = [None] * len(bones)
    count = [0] * len(bones)
    for v in m.vertices:
        k = owner[v.index]
        centre[k] = v.co.copy() if centre[k] is None else centre[k] + v.co
        count[k] += 1
    centre = [c / n if n else None for c, n in zip(centre, count)]
    outward = [0.0] * len(bones)
    for tri in m.loop_triangles:
        a, b, c = (m.vertices[i].co for i in tri.vertices)
        k = owner[tri.vertices[0]]
        outward[k] += (b - a).cross(c - a).dot((a + b + c) / 3 - centre[k])
    inside_out = {k for k in range(len(bones)) if count[k] and outward[k] < 0}
    if inside_out:
        print("  %s: bones %s were inside out, flipped" % (o.name, ", ".join(bones[k][0] for k in sorted(inside_out))))
    per = [{"pos": [], "nrm": [], "index_of": {}, "subs": {}} for _ in bones]
    for tri in m.loop_triangles:
        b = owner[tri.vertices[0]]
        assert all(owner[v] == b for v in tri.vertices), "%s: triangle across bones" % o.name
        d = per[b]
        sub = d["subs"].setdefault(material(o.data.materials[tri.material_index]), [])
        flip = owner[tri.vertices[0]] in inside_out
        corners = list(zip(tri.loops, tri.vertices))
        if flip:
            corners.reverse()
        for loop, v in corners:
            n = (nmat @ normals[loop].vector).normalized()
            if flip:
                n = -n
            key = (v, round(n.x, 4), round(n.y, 4), round(n.z, 4))
            k = d["index_of"].get(key)
            if k is None:
                k = len(d["pos"])
                d["index_of"][key] = k
                d["pos"].append(mw @ m.vertices[v].co - origin)
                d["nrm"].append(n)
            sub.append(k)
    e.to_mesh_clear()
    return kind, hook, hook_bone, bones, per


def s(f, text):
    raw = text.encode("utf-8")
    f.write(struct.pack("<B", len(raw)))
    f.write(raw)


def v3(f, v):
    f.write(struct.pack("<3f", v.x, v.y, v.z))


models = []
for obj_name, model_id in IDS.items():
    o = bpy.data.objects[obj_name]
    rig = bpy.data.objects.get(obj_name + "_rig")
    assert rig is not None or model_id == "link", obj_name + " is not rigged (run rig_piercings.py first)"
    models.append((model_id, export(o, rig)))

with open(out_path, "wb") as f:
    f.write(b"NPPD")
    f.write(struct.pack("<I", 2))
    f.write(struct.pack("<I", len(materials)))
    for name, colour, metallic, transmission in materials:
        s(f, name)
        f.write(struct.pack("<4f", *colour))
        f.write(struct.pack("<2f", metallic, transmission))
    f.write(struct.pack("<I", len(models)))
    for model_id, (kind, hook, hook_bone, bones, per) in models:
        s(f, model_id)
        f.write(struct.pack("<B", kind))
        v3(f, hook)
        f.write(struct.pack("<I", hook_bone))
        f.write(struct.pack("<I", len(bones)))
        for i, (name, parent, head, tail) in enumerate(bones):
            d = per[i]
            s(f, name)
            f.write(struct.pack("<i", parent))
            v3(f, head)
            v3(f, tail)
            f.write(struct.pack("<I", len(d["pos"])))
            for p in d["pos"]:
                v3(f, p)
            for n in d["nrm"]:
                v3(f, n)
            f.write(struct.pack("<I", len(d["subs"])))
            for mi, idx in sorted(d["subs"].items()):
                f.write(struct.pack("<II", mi, len(idx)))
                f.write(struct.pack("<%dI" % len(idx), *idx))
        print("%-15s kind %d bones %d vertices %6d" % (model_id, kind, len(bones), sum(len(d["pos"]) for d in per)))
print("materials", [m[0] for m in materials])
print("written", out_path, os.path.getsize(out_path), "bytes")
