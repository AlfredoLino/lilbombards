"""
Genera los modelos 3D de Lil Bombards y los exporta como FBX a
Assets/Resources/Models, donde el juego los detecta automaticamente
(si no existen, el juego usa primitivas de Unity).

Uso (Blender 4.x):
    blender --background --python Blender/generate_assets.py

Tambien guarda Blender/lilbombards_assets.blend para editarlos a mano.
Si editas un modelo, vuelve a exportarlo con el mismo nombre y los mismos
ajustes (ver export_fbx). Convenciones:
  * El frente del objeto mira hacia -Y en Blender (vista frontal = Numpad 1).
  * La escala no importa: Unity normaliza cada malla a tamano 1 y la reescala.
  * Un solo objeto y un solo material por archivo; el color lo pone el juego.
"""

import math
import os

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Resources", "Models"))


# --------------------------------------------------------------------------- utilidades

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def uv_sphere(name, radius=0.5, segments=48, rings=24, loc=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    return obj


def cube(name, size=1.0, loc=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_cube_add(size=size, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    return obj


def cylinder(name, radius=0.5, depth=1.0, vertices=48, loc=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    return obj


def apply_transform(obj):
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def subsurf(obj, levels=2):
    m = obj.modifiers.new("Subsurf", "SUBSURF")
    m.levels = levels
    m.render_levels = levels
    return m


def bevel(obj, width=0.08, segments=4):
    m = obj.modifiers.new("Bevel", "BEVEL")
    m.width = width
    m.segments = segments
    m.limit_method = "ANGLE"
    m.harden_normals = False
    return m


def smooth(obj):
    activate(obj)
    bpy.ops.object.shade_smooth()


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    return obj


def deform(obj, fn):
    """Aplica fn(x, y, z) -> (x, y, z) a todos los vertices."""
    apply_transform(obj)
    me = obj.data
    for v in me.vertices:
        v.co = fn(v.co.x, v.co.y, v.co.z)
    me.update()


def reset_uvs_per_face(obj):
    """Cada cara recibe la textura completa (0..1), como el cubo de Unity."""
    activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.reset()
    bpy.ops.object.mode_set(mode="OBJECT")


def material(obj, name, color):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def export_fbx(obj, filename):
    os.makedirs(OUT, exist_ok=True)
    activate(obj)
    path = os.path.join(OUT, filename + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        add_leaf_bones=False,
        bake_anim=False,
        use_custom_props=False,
    )
    print("[LilBombards] Exportado", path)


# --------------------------------------------------------------------------- modelos

def make_head():
    """Cabeza grande y redonda: craneo ancho, mandibula un poco mas estrecha."""
    head = uv_sphere("head", 0.5, 64, 32)

    def f(x, y, z):
        k = 1.04 if z > 0 else 1.0 - 0.1 * min(1.0, -z / 0.5)
        y2 = y * (0.94 if y < 0 else 1.0)  # cara (frente = -Y) ligeramente aplanada
        return (x * k, y2 * k, z * 0.98)

    deform(head, f)
    smooth(head)
    material(head, "Skin", (1.0, 0.8, 0.64))
    return head


def make_torso():
    """Torso con hombros anchos que se estrecha hacia la cintura."""
    t = cube("torso", 1.0, scale=(0.54, 0.42, 0.5))
    apply_transform(t)
    subsurf(t, 3)
    activate(t)
    bpy.ops.object.modifier_apply(modifier="Subsurf")

    def f(x, y, z):
        k = 1.0 + 0.18 * (z / 0.25)  # mas ancho arriba
        k = max(0.75, min(1.2, k))
        return (x * k, y * (0.95 + 0.05 * k), z)

    deform(t, f)
    smooth(t)
    material(t, "Shirt", (0.9, 0.2, 0.2))
    return t


def make_pelvis():
    p = cube("pelvis", 1.0, scale=(0.44, 0.36, 0.32))
    apply_transform(p)
    subsurf(p, 3)
    smooth(p)
    material(p, "Pants", (0.2, 0.2, 0.3))
    return p


def make_hand():
    """Mano tipo manopla con pulgar hacia arriba (simetrica, sirve para ambos lados)."""
    palm = uv_sphere("palm", 0.5, 32, 16, scale=(1.0, 0.85, 0.95))
    thumb = uv_sphere("thumb", 0.22, 24, 12, loc=(0.0, -0.18, 0.42), scale=(0.9, 0.9, 1.2))
    for o in (palm, thumb):
        apply_transform(o)
    hand = join([palm, thumb], "hand")
    smooth(hand)
    material(hand, "Skin", (1.0, 0.8, 0.64))
    return hand


def make_foot():
    """Zapato redondeado con la punta levantada (frente = -Y)."""
    f = cube("foot", 1.0, scale=(0.2, 0.3, 0.13))
    apply_transform(f)
    subsurf(f, 3)
    activate(f)
    bpy.ops.object.modifier_apply(modifier="Subsurf")

    def g(x, y, z):
        front = max(0.0, -y / 0.15)  # 0 atras, 1 en la punta
        z2 = z + 0.03 * front * front
        x2 = x * (1.0 + 0.15 * front)
        z2 = z2 if z2 > -0.06 else -0.06 + (z2 + 0.06) * 0.3  # suela plana
        return (x2, y, z2)

    deform(f, g)
    smooth(f)
    material(f, "Shoe", (0.15, 0.15, 0.2))
    return f


def make_bomb():
    """Bomba esferica con un aro donde va la mecha (la mecha la anade el juego)."""
    ball = uv_sphere("ball", 0.5, 64, 32)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.17, minor_radius=0.045, location=(0, 0, 0.47))
    ring = bpy.context.active_object
    bomb = join([ball, ring], "bomb")
    smooth(bomb)
    material(bomb, "Bomb", (0.1, 0.1, 0.12))
    return bomb


def make_landmine():
    body = cylinder("mine", 0.5, 0.22, 48)
    bevel(body, 0.06, 4)
    activate(body)
    bpy.ops.object.modifier_apply(modifier="Bevel")
    ridges = []
    for i in range(8):
        a = i * math.pi / 4.0
        r = cube("ridge", 1.0, loc=(math.cos(a) * 0.36, math.sin(a) * 0.36, 0.11), scale=(0.08, 0.08, 0.03))
        r.rotation_euler = (0, 0, a)
        apply_transform(r)
        ridges.append(r)
    mine = join([body] + ridges, "landmine")
    smooth(mine)
    material(mine, "Mine", (0.4, 0.42, 0.46))
    return mine


def make_box(name):
    """Caja con bordes biselados y UV 0..1 por cara (para iconos / TNT)."""
    box = cube(name, 1.0)
    reset_uvs_per_face(box)
    bevel(box, 0.07, 3)
    smooth(box)
    material(box, "Box", (1, 1, 1))
    return box


# --------------------------------------------------------------------------- main

def main():
    reset_scene()
    builders = [
        ("head", make_head),
        ("torso", make_torso),
        ("pelvis", make_pelvis),
        ("hand", make_hand),
        ("foot", make_foot),
        ("bomb", make_bomb),
        ("landmine", make_landmine),
        ("powerup", lambda: make_box("powerup")),
        ("tnt", lambda: make_box("tnt")),
    ]
    spacing = 1.6
    for i, (name, fn) in enumerate(builders):
        obj = fn()
        obj.name = name
        obj.location = (0, 0, 0)
        export_fbx(obj, name)
        obj.location = (i * spacing, 0, 0)  # separados en el .blend para editarlos

    blend_path = os.path.join(HERE, "lilbombards_assets.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print("[LilBombards] Guardado", blend_path)


if __name__ == "__main__":
    main()
