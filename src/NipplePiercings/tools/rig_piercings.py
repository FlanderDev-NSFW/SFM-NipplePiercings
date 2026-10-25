"""Rig every piercing of Piercings.blend: one bone per rigid piece, saved as a new .blend (the source stays untouched).

Usage (Blender 5.x, headless):
    blender -b --disable-autoexec "<Piercings.blend>" --python mods/NipplePiercings/tools/rig_piercings.py -- <out.blend>

Each piercing (one mesh object, made of loose pieces) gets an armature "<object>_rig" in its collection and an Armature modifier
(before its Subdivision); its pieces are weighted 1.0 to their bone (new vertex groups named after the bones; the original
groups stay). Bones form one chain top to bottom: the first is fixed to the nipple, the rest hang from it:
  - studs and the crystal pendant sit on the nipple's tip: bone "stud" (the ball), then the chain links and the pendant;
  - barbells go through the nipple: one bone "bar", fixed;
  - rings go through the nipple and swing about it: a short bone "pierce" (no mesh) ending where the ring's wire passes through
    the nipple, then "ring" down to the ring's bottom;
  - the heart pendant: its curved barbell "bar" ends at the lower ball, then six links and the heart.
A joint between two pieces is the middle of their closest pair of vertices (where they interlock).
Custom properties on the armature tell the exporter how to place it: "kind" ("tip" or "through"), "attach" (the point that goes
on the nipple's tip / in the pierced channel), "hook" and "hook_bone" (where a chain between the nipples hangs from).
"""
import collections
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
out_path = os.path.abspath(args[0] if args else "Piercings_rigged.blend")


def pieces_of(o):
    """Loose pieces: vertex ids, world points, original vertex group."""
    me, mw = o.data, o.matrix_world
    names = {g.index: g.name for g in o.vertex_groups}
    adj = collections.defaultdict(set)
    for e in me.edges:
        a, b = e.vertices
        adj[a].add(b)
        adj[b].add(a)
    seen, out = set(), []
    for v in range(len(me.vertices)):
        if v in seen:
            continue
        stack, ids = [v], []
        seen.add(v)
        while stack:
            x = stack.pop()
            ids.append(x)
            for y in adj[x]:
                if y not in seen:
                    seen.add(y)
                    stack.append(y)
        pts = [mw @ me.vertices[i].co for i in ids]
        groups = sorted({names[g.group] for i in ids for g in me.vertices[i].groups})
        out.append({"ids": ids, "pts": pts, "group": groups[0] if groups else "",
                    "centre": sum(pts, Vector()) / len(pts),
                    "lo": Vector([min(p[k] for p in pts) for k in range(3)]),
                    "hi": Vector([max(p[k] for p in pts) for k in range(3)])})
    return out


def merge(ps):
    pts = [p for x in ps for p in x["pts"]]
    return {"ids": [i for x in ps for i in x["ids"]], "pts": pts, "centre": sum(pts, Vector()) / len(pts),
            "lo": Vector([min(p[k] for p in pts) for k in range(3)]), "hi": Vector([max(p[k] for p in pts) for k in range(3)])}


def joint(a, b):
    """The middle of the closest pair of vertices of two pieces."""
    best, pair = None, None
    for p in a["pts"]:
        for q in b["pts"]:
            d = (p - q).length_squared
            if best is None or d < best:
                best, pair = d, (p, q)
    return (pair[0] + pair[1]) / 2


def lowest(piece):
    return min(piece["pts"], key=lambda p: p.z)


def bar_middle(bar):
    """A curved / straight bar's centreline in the middle of its length (height for vertical bars, x for horizontal ones)."""
    ext = bar["hi"] - bar["lo"]
    axis = 2 if ext.z >= ext.x else 0
    mid = (bar["lo"][axis] + bar["hi"][axis]) / 2
    near = [p for p in bar["pts"] if abs(p[axis] - mid) < ext[axis] * 0.08]
    return sum(near, Vector()) / len(near)


def chain_links(pieces):
    """Chain pieces grouped into links top to bottom: big pieces are links, small ones join the nearest link."""
    if all(len(p["ids"]) <= 16 for p in pieces):   # the crystal pendant: two pieces per link at the same height
        by = collections.defaultdict(list)
        for p in pieces:
            by[round(p["centre"].z, 3)].append(p)
        return [merge(by[z]) for z in sorted(by, reverse=True)]
    big = [p for p in pieces if len(p["ids"]) >= 100]
    small = [p for p in pieces if len(p["ids"]) < 100]
    groups = [[b] for b in big]
    for s in small:
        k = min(range(len(big)), key=lambda i: (big[i]["centre"] - s["centre"]).length)
        groups[k].append(s)
    links = [merge(g) for g in groups]
    return sorted(links, key=lambda l: -l["centre"].z)


def recipe(o):
    """(kind, attach, segments [(bone, piece or None, head, tail)], hook, hook bone) for one piercing."""
    ps = pieces_of(o)
    name = o.name
    if name in ("piercing_stud",):
        ball = merge(ps)
        r = (ball["hi"].z - ball["lo"].z) / 2
        c = (ball["lo"] + ball["hi"]) / 2
        return "tip", c, [("stud", ball, c, c - Vector((0, 0, r)))], c - Vector((0, 0, r)), 0
    if name == "piercing_pendant_crystal":
        stud = merge([p for p in ps if p["group"] == "piercing"])
        links = chain_links([p for p in ps if p["group"] == "chain"])
        pendant = merge([p for p in ps if p["group"] == "pendant"])
        c = stud["centre"]
        segs = [("stud", stud), *[("link%d" % (i + 1), l) for i, l in enumerate(links)], ("pendant", pendant)]
        heads = [c] + [joint(segs[k - 1][1], segs[k][1]) for k in range(1, len(segs))]
        tails = heads[1:] + [lowest(pendant)]
        return "tip", c, [(n, p, h, t) for (n, p), h, t in zip(segs, heads, tails)], c - Vector((0, 0, (stud["hi"].z - stud["lo"].z) / 2)), 0
    if name in ("piercing_barbell", "piercing_barbell_curved", "piercing_barbell_curved_small"):
        bar_piece = max(ps, key=lambda p: max(p["hi"] - p["lo"]) / max(1e-6, min(p["hi"] - p["lo"])))  # the thin, long piece
        whole = merge(ps)
        attach = bar_middle(bar_piece) if len(ps) > 1 else (whole["lo"] + whole["hi"]) / 2
        balls = sorted([p for p in ps if p is not bar_piece], key=lambda p: p["centre"].z)
        hook = balls[0]["centre"] if balls and name != "piercing_barbell" else attach - Vector((0, 0, (whole["hi"].z - whole["lo"].z) / 2))
        return "through", attach, [("bar", whole, attach, attach - Vector((0, 0, 0.02)))], hook, 0
    if name == "piercing_pendant":
        bar = merge([p for p in ps if p["group"] == "piercing"])
        bar_pieces = [p for p in ps if p["group"] == "piercing"]
        bar_piece = max(bar_pieces, key=lambda p: max(p["hi"] - p["lo"]) / max(1e-6, min(p["hi"] - p["lo"])))
        attach = bar_middle(bar_piece)
        links = chain_links([p for p in ps if p["group"] == "chain"])
        pendant = merge([p for p in ps if p["group"] == "pendant"])
        segs = [("bar", bar), *[("link%d" % (i + 1), l) for i, l in enumerate(links)], ("pendant", pendant)]
        heads = [attach] + [joint(segs[k - 1][1], segs[k][1]) for k in range(1, len(segs))]
        tails = heads[1:] + [lowest(pendant)]
        return "through", attach, [(n, p, h, t) for (n, p), h, t in zip(segs, heads, tails)], heads[1], 0
    if name in ("piercing_ring_nipple", "piercing_ring_small", "piercing_loop_big", "piercing_chain_ring", "piercing_chain_ring_big"):
        whole = merge(ps)
        ring = max(ps, key=lambda p: len(p["ids"]))
        wire = (ring["hi"].y - ring["lo"].y) / 2                 # the wire's radius (the ring lies in the x-z plane)
        x = (ring["lo"].x + ring["hi"].x) / 2
        y = (ring["lo"].y + ring["hi"].y) / 2
        top = Vector((x, y, ring["hi"].z - wire))
        bottom = Vector((x, y, whole["lo"].z + wire))
        return "through", top, [("pierce", None, top + Vector((0, 0, wire * 2)), top), ("ring", whole, top, bottom)], bottom, 1
    return None


summary = []
for o in [x for x in bpy.data.objects if x.type == 'MESH']:
    if o.name == "piercing_chain_link":
        continue  # used as is, as a link of the chain between the nipples
    r = recipe(o)
    if r is None:
        print("skipped", o.name)
        continue
    kind, attach, segs, hook, hook_bone = r
    coll = o.users_collection[0]
    arm = bpy.data.armatures.new(o.name + "_rig")
    rig = bpy.data.objects.new(o.name + "_rig", arm)
    coll.objects.link(rig)
    rig["kind"] = kind
    rig["attach"] = list(attach)
    rig["hook"] = list(hook)
    rig["hook_bone"] = hook_bone
    bpy.context.view_layer.objects.active = rig
    for ob in bpy.context.view_layer.objects:
        ob.select_set(ob == rig)
    bpy.ops.object.mode_set(mode='EDIT')
    prev = None
    for bname, _, h, t in segs:
        b = arm.edit_bones.new(bname)
        b.head, b.tail = h, t
        if (t - h).length < 1e-4:
            b.tail = h - Vector((0, 0, 0.01))
        if prev is not None:
            b.parent = prev
            b.use_connect = (prev.tail - b.head).length < 1e-5
        prev = b
    bpy.ops.object.mode_set(mode='OBJECT')
    for bname, piece, _, _ in segs:
        if piece is None:
            continue
        g = o.vertex_groups.get(bname) or o.vertex_groups.new(name=bname)
        g.add(piece["ids"], 1.0, 'REPLACE')
    mod = o.modifiers.new("rig", 'ARMATURE')
    mod.object = rig
    o.modifiers.move(o.modifiers.find(mod.name), 0)
    o.parent = rig
    o.matrix_parent_inverse = rig.matrix_world.inverted()
    summary.append("%-32s %-7s bones %s" % (o.name, kind, ", ".join("%s %.3f" % (b, (t - h).length) for b, _, h, t in segs)))

print("\n".join(summary))
bpy.ops.wm.save_as_mainfile(filepath=out_path, copy=True)
print("saved", out_path)
