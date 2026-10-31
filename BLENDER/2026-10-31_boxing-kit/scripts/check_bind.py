"""Checks that each Kit_<Name>.glb binds to the boxer's own skeleton: for every joint the kit uses, its inverse
bind matrix must equal the one in Assets/Models/<Name>.glb, and every vertex's joints must name bones the boxer has.
Run: blender -b --python check_bind.py"""
import json, struct, numpy as np
ROOT = "C:/Users/punko/Downloads/PoBox/"
def glb(path):
    b = open(path, "rb").read()
    jl = struct.unpack_from("<I", b, 12)[0]; js = json.loads(b[20:20 + jl])
    bin_ = b[20 + jl + 8:]
    return js, bin_
def acc(js, bin_, i):
    a = js["accessors"][i]; bv = js["bufferViews"][a["bufferView"]]
    n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
    dt = {5126: np.float32, 5123: np.uint16, 5121: np.uint8, 5125: np.uint32}[a["componentType"]]
    off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    return np.frombuffer(bin_, dt, a["count"] * n, off).reshape(a["count"], n)
def ibms(js, bin_):
    out = {}
    for sk in js.get("skins", []):
        m = acc(js, bin_, sk["inverseBindMatrices"])
        for j, M in zip(sk["joints"], m): out.setdefault(js["nodes"][j]["name"], M.reshape(4, 4).T)
    return out
res = {}
for n in ["Matt", "Zombie", "Nick", "Trump", "Grandma", "Grandpa", "Kim", "Sokol"]:
    try: kj, kb = glb(ROOT + "BLENDER/2026-10-31_boxing-kit/Kit_%s.glb" % n)
    except FileNotFoundError: continue
    oj, ob = glb(ROOT + "Assets/Models/%s.glb" % n)
    ki, oi = ibms(kj, kb), ibms(oj, ob)
    onodes = {nd.get("name") for nd in oj["nodes"]}
    # joints actually weighted
    sk = kj["skins"][0]; used = set()
    for mesh in kj["meshes"]:
        for p in mesh["primitives"]:
            J = acc(kj, kb, p["attributes"]["JOINTS_0"]); W = acc(kj, kb, p["attributes"]["WEIGHTS_0"])
            for jj, ww in zip(J, W):
                for a, w in zip(jj, ww):
                    if w > 1e-4: used.add(kj["nodes"][sk["joints"][a]]["name"])
    diffs = {j: float(np.abs(ki[j] - oi[j]).max()) for j in used if j in oi}
    missing = sorted(j for j in used if j not in onodes)
    tris = sum(acc(kj, kb, p["indices"]).size // 3 for m in kj["meshes"] for p in m["primitives"])
    mats = [m["name"] for m in kj.get("materials", [])]
    res[n] = dict(tris=tris, joints_used=len(used), max_ibm_diff=max(diffs.values()) if diffs else None,
                  joints_without_orig_ibm=sorted(j for j in used if j not in oi), missing_in_boxer=missing, materials=mats,
                  meshes=[m["name"] for m in kj["meshes"]], nodes=len(kj["nodes"]))
    print("[bind]", n, res[n], flush=True)
json.dump(res, open(ROOT + "BLENDER/2026-10-31_boxing-kit/scripts/check_bind.json", "w"), indent=1)
