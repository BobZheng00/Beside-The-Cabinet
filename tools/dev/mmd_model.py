"""A small stand-in for a MikuMikuDance (MMD) model, to try adding one without a real model's licence in the way.

    python tools/dev/mmd_model.py <out.glb> [--plain]

Writes a GLB of a blocky figure skinned to a skeleton with MMD's standard Japanese bone names, as Blender's MMD
Tools exports them: sides as .L/.R, the semi-standard 腰, the legs' D bones (which carry the skin; the plain leg
bones carry none), 腰キャンセル, 肩P/肩C, twist bones, IK bones and MMD Tools' _dummy_/_shadow_ helpers.
--plain writes an older model's skeleton instead: no 腰 (上半身 and 下半身 hang from グルーブ), no D bones, sides as
左/右 in front ("左腕") and full-width finger numbers ("人指１").

Then `add:<out.glb>` in a test run adds it (tools/dev/README.md). Only the standard library is used.
"""
import json
import struct
import sys

plain = '--plain' in sys.argv
out = next(a for a in sys.argv[1:] if not a.startswith('--'))

bones = []          # (name, parent index, global position, weighted)


def bone(name, parent, at, weighted=False):
    bones.append((name, parent, at, weighted))
    return len(bones) - 1


def side_name(word, side, number=''):
    if plain:
        digits = number.translate(str.maketrans('0123456789', '０１２３４５６７８９'))
        return ('左' if side > 0 else '右') + word + digits
    return f"{word}{number}.{'L' if side > 0 else 'R'}"


root = bone('全ての親', -1, (0, 0, 0))
centre = bone('センター', root, (0, 0.9, 0))
groove = bone('グルーブ', centre, (0, 0.95, 0))
waist = groove if plain else bone('腰', groove, (0, 1.0, 0))
upper = bone('上半身', waist, (0, 1.02, 0), True)
upper2 = bone('上半身2', upper, (0, 1.2, 0), True)
neck = bone('首', upper2, (0, 1.4, 0), True)
head = bone('頭', neck, (0, 1.48, 0), True)
lower = bone('下半身', waist, (0, 1.02, 0), True)
for side in (1, -1):
    x = side
    bone(side_name('目', side), head, (0.03 * x, 1.56, 0.08))
    shoulder_p = bone(side_name('肩P', side), upper2, (0.02 * x, 1.37, 0))
    shoulder = bone(side_name('肩', side), shoulder_p, (0.02 * x, 1.37, 0), True)
    cancel = bone(side_name('肩C', side), shoulder, (0.15 * x, 1.37, 0))
    arm = bone(side_name('腕', side), cancel, (0.15 * x, 1.37, 0), True)
    arm_twist = bone(side_name('腕捩', side), arm, (0.27 * x, 1.37, 0))
    if not plain:
        bone('_dummy_' + side_name('腕捩', side, '1'), arm_twist, (0.2 * x, 1.37, 0))
        bone('_shadow_' + side_name('腕捩', side, '1'), arm, (0.2 * x, 1.37, 0))
    elbow = bone(side_name('ひじ', side), arm_twist, (0.4 * x, 1.37, 0), True)
    hand_twist = bone(side_name('手捩', side), elbow, (0.52 * x, 1.37, 0))
    wrist = bone(side_name('手首', side), hand_twist, (0.64 * x, 1.37, 0), True)
    bone(side_name('手先', side), wrist, (0.74 * x, 1.37, 0))
    for word, numbers, z in (('親指', '012', 0.035), ('人指', '123', 0.02), ('中指', '123', 0.005), ('薬指', '123', -0.01), ('小指', '123', -0.025)):
        at = wrist
        for k, number in enumerate(numbers):
            at = bone(side_name(word, side, number), at, ((0.66 + 0.025 * k + (0.02 if word != '親指' else 0)) * x, 1.37, z), True)
    hip_side = lower if plain else bone(side_name('腰キャンセル', side), lower, (0.09 * x, 0.92, 0))
    for d in ('', 'D') if not plain else ('',):
        weighted = plain or d == 'D'
        leg = bone(side_name('足' + d, side), hip_side, (0.09 * x, 0.92, 0), weighted)
        knee = bone(side_name('ひざ' + d, side), leg, (0.09 * x, 0.5, 0), weighted)
        ankle = bone(side_name('足首' + d, side), knee, (0.09 * x, 0.08, 0), weighted)
        bone(side_name('足先EX' if d else 'つま先', side), ankle, (0.09 * x, 0.02, 0.12), weighted)
        if d:
            bone('_dummy_' + side_name('足首D', side), plain_ankle, (0.09 * x, 0.08, 0))
        plain_ankle = ankle
    ik_parent = bone(side_name('足IK親', side), root, (0.09 * x, 0, 0))
    ik = bone(side_name('足ＩＫ', side), ik_parent, (0.09 * x, 0.08, 0))
    bone(side_name('つま先ＩＫ', side), ik, (0.09 * x, 0.02, 0.12))

children = {i: [j for j, b in enumerate(bones) if b[1] == i] for i in range(len(bones))}

# A box along each weighted bone, to its first weighted child (or a small one at a leaf), skinned to that bone.
positions, normals, joints, indices = [], [], [], []
for i, (name, parent, at, weighted) in enumerate(bones):
    if not weighted:
        continue
    # to the nearest weighted bone below, past twist and cancel bones (腕 to ひじ)
    below, end = list(children[i]), None
    while below and end is None:
        c = below.pop(0)
        end = c if bones[c][3] else None
        below += children[c]
    if i == head:
        lo, hi = (-0.1, 1.46, -0.1), (0.1, 1.68, 0.1)
    else:
        to = bones[end][2] if end is not None else at
        r = 0.012 if '指' in name else 0.05 if i in (upper, upper2, lower) else 0.03
        lo = tuple(min(a, b) - r for a, b in zip(at, to))
        hi = tuple(max(a, b) + r for a, b in zip(at, to))
        if i == lower:
            lo, hi = (-0.15, 0.85, -0.08), (0.15, 1.05, 0.08)
    for axis in range(3):
        for sign in (-1, 1):
            n = [0, 0, 0]
            n[axis] = sign
            u, v = [a for a in range(3) if a != axis]
            base = len(positions)
            for a, b in ((0, 0), (1, 0), (1, 1), (0, 1)):
                p = [0, 0, 0]
                p[axis] = hi[axis] if sign > 0 else lo[axis]
                p[u] = hi[u] if a else lo[u]
                p[v] = hi[v] if b else lo[v]
                positions.append(p)
                normals.append(n)
                joints.append((i, 0, 0, 0))
            # wind each face counter-clockwise seen from outside (glTF's front face)
            e1 = [positions[base + 1][k] - positions[base][k] for k in range(3)]
            e2 = [positions[base + 2][k] - positions[base][k] for k in range(3)]
            facing = sum((e1[(k + 1) % 3] * e2[(k + 2) % 3] - e1[(k + 2) % 3] * e2[(k + 1) % 3]) * n[k] for k in range(3))
            quad = [base, base + 1, base + 2, base, base + 2, base + 3]
            indices.extend(quad if facing > 0 else quad[::-1])

blob = bytearray()
views, accessors = [], []


def add(data, fmt, count, kind, component, target=None, minmax=None):
    while len(blob) % 4:
        blob.append(0)
    offset = len(blob)
    blob.extend(data)
    view = {'buffer': 0, 'byteOffset': offset, 'byteLength': len(data)}
    if target:
        view['target'] = target
    views.append(view)
    accessor = {'bufferView': len(views) - 1, 'componentType': component, 'count': count, 'type': kind}
    if minmax:
        accessor['min'], accessor['max'] = minmax
    accessors.append(accessor)
    return len(accessors) - 1


flat = [c for p in positions for c in p]
pos = add(struct.pack(f'<{len(flat)}f', *flat), None, len(positions), 'VEC3', 5126, 34962,
          ([min(p[k] for p in positions) for k in range(3)], [max(p[k] for p in positions) for k in range(3)]))
nor = add(struct.pack(f'<{len(normals) * 3}f', *[c for n in normals for c in n]), None, len(normals), 'VEC3', 5126, 34962)
jnt = add(struct.pack(f'<{len(joints) * 4}H', *[c for j in joints for c in j]), None, len(joints), 'VEC4', 5123, 34962)
wgt = add(struct.pack(f'<{len(joints) * 4}f', *[1, 0, 0, 0] * len(joints)), None, len(joints), 'VEC4', 5126, 34962)
idx = add(struct.pack(f'<{len(indices)}I', *indices), None, len(indices), 'SCALAR', 5125, 34963)
inverse = []
for _, _, at, _ in bones:
    inverse += [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -at[0], -at[1], -at[2], 1]
ibm = add(struct.pack(f'<{len(inverse)}f', *inverse), None, len(bones), 'MAT4', 5126)

nodes = []
for i, (name, parent, at, _) in enumerate(bones):
    local = at if parent < 0 else tuple(a - b for a, b in zip(at, bones[parent][2]))
    node = {'name': name, 'translation': list(local)}
    if children[i]:
        node['children'] = children[i]
    nodes.append(node)
nodes.append({'name': 'body', 'mesh': 0, 'skin': 0})
nodes.append({'name': 'mmd_figure', 'children': [0, len(nodes) - 1]})
document = {
    'asset': {'version': '2.0', 'generator': 'tools/dev/mmd_model.py'},
    'scene': 0, 'scenes': [{'nodes': [len(nodes) - 1]}],
    'nodes': nodes,
    'meshes': [{'name': 'body', 'primitives': [{
        'attributes': {'POSITION': pos, 'NORMAL': nor, 'JOINTS_0': jnt, 'WEIGHTS_0': wgt}, 'indices': idx, 'material': 0}]}],
    'materials': [{'name': 'skin', 'pbrMetallicRoughness': {'baseColorFactor': [0.95, 0.8, 0.7, 1], 'metallicFactor': 0}}],
    'skins': [{'joints': list(range(len(bones))), 'skeleton': 0, 'inverseBindMatrices': ibm}],
    'accessors': accessors, 'bufferViews': views, 'buffers': [{'byteLength': len(blob)}],
}
text = json.dumps(document, ensure_ascii=False).encode('utf-8')
text += b' ' * (-len(text) % 4)
while len(blob) % 4:
    blob.append(0)
with open(out, 'wb') as f:
    f.write(struct.pack('<III', 0x46546C67, 2, 12 + 8 + len(text) + 8 + len(blob)))
    f.write(struct.pack('<II', len(text), 0x4E4F534A) + text)
    f.write(struct.pack('<II', len(blob), 0x004E4942) + blob)
print(f'{out}: {len(bones)} bones, {len(positions) // 24} boxes')
