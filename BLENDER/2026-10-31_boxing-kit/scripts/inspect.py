import bpy, sys, os
from mathutils import Vector
M="C:/Users/punko/Downloads/PoBox/Assets/Models/"
for n in ["Matt","Zombie","Nick","Trump","Grandma","Grandpa","Kim","Sokol"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=M+n+".glb")
    print("=====",n)
    for o in bpy.data.objects:
        if o.type=='MESH':
            bb=[o.matrix_world@Vector(c) for c in o.bound_box]
            mn=Vector((min(v.x for v in bb),min(v.y for v in bb),min(v.z for v in bb)));mx=Vector((max(v.x for v in bb),max(v.y for v in bb),max(v.z for v in bb)))
            print("MESH",o.name,len(o.data.polygons),"parent",o.parent.name if o.parent else None,"mods",[m.type for m in o.modifiers],"vg",len(o.vertex_groups),"bb",tuple(round(x,3) for x in mn),tuple(round(x,3) for x in mx))
        elif o.type=='ARMATURE':
            print("ARM",o.name,"parent",o.parent.name if o.parent else None,"mw",[tuple(round(x,3) for x in r) for r in o.matrix_world])
            print("  bones:",[b.name for b in o.data.bones])
        else:
            print(o.type,o.name,"parent",o.parent.name if o.parent else None)
