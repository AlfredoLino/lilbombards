"""
Genera las piezas del cuerpo por capas (armadura, piel, musculo, hueso) del piloto de
motocross de Lil Bombards y las exporta como FBX a Assets/Resources/Models/body/.

Uso (Blender 4.x / 5.x):
    blender --background --python Blender/generate_body.py

Convenciones (importantes: el juego coloca las piezas tal cual, SIN normalizar):
  * Unidades en metros, Z arriba, el frente del personaje mira hacia -Y.
  * Piezas de CABEZA: origen = centro de la cabeza (ojos en x=+-0.082, y=-0.272, z=0.02).
  * Piezas de TORSO: origen = pies del personaje (coordenadas del cuerpo; pecho en z~0.84).
  * Piezas de EXTREMIDAD (limb_*): segmento unitario a lo largo de Z de -1 a 1, radio 0.5
    (el juego lo estira entre dos articulaciones, igual que una capsula).
  * MANOS: origen = centro de la mano (~0.17 m). PIES: origen = punto del pie, suela en z=-0.07.
  * Rodillera / hombrera: origen = centro de la pieza.
Tambien guarda Blender/lilbombards_body.blend y una vista previa en Blender/preview_body.png.
"""

import math
import os

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "Assets", "Resources", "Models", "body"))

# Colores solo para la vista previa (en el juego los pone el codigo).
C_ARMOR = (0.95, 0.25, 0.2, 1)
C_ACCENT = (1.0, 0.85, 0.25, 1)
C_DARK = (0.08, 0.08, 0.1, 1)
C_JERSEY = (0.8, 0.2, 0.18, 1)
C_PANTS = (0.18, 0.18, 0.22, 1)
C_BOOT = (0.93, 0.93, 0.95, 1)
C_SKIN = (1.0, 0.78, 0.62, 1)
C_HAIR = (0.28, 0.16, 0.08, 1)
C_MUSCLE = (0.78, 0.16, 0.14, 1)
C_CAVITY = (0.35, 0.05, 0.06, 1)
C_BONE = (0.95, 0.92, 0.82, 1)
C_TEETH = (1.0, 1.0, 0.97, 1)

LIB = {}  # nombre -> (objeto, color)


# =========================================================================== utilidades

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def apply_all(obj):
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def sphere(r=0.5, loc=(0, 0, 0), scale=(1, 1, 1), seg=32, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=r, location=loc)
    o = bpy.context.active_object
    o.scale = scale
    apply_all(o)
    return o


def cube(size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, seg=3):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = size
    apply_all(o)
    if bevel > 0:
        modifier(o, "BEVEL", width=bevel, segments=seg)
    return o


def cylinder(r=0.5, depth=1.0, loc=(0, 0, 0), rot=(0, 0, 0), verts=24):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object
    apply_all(o)
    return o


def torus(major, minor, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), seg=40, mseg=10):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=seg,
                                     minor_segments=mseg, location=loc, rotation=rot)
    o = bpy.context.active_object
    o.scale = scale
    apply_all(o)
    return o


def modifier(obj, kind, **props):
    m = obj.modifiers.new(kind.lower(), kind)
    for k, v in props.items():
        setattr(m, k, v)
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=m.name)
    return obj


def solidify(obj, t):
    return modifier(obj, "SOLIDIFY", thickness=t, offset=-1.0)


def keep_faces(obj, pred):
    """Borra las caras cuyo centro NO cumple pred(Vector)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    kill = [f for f in bm.faces if not pred(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


def cap(obj):
    """Cierra los agujeros (corte de media esfera) con una tapa plana."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


def join(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    return bpy.context.active_object


def deform(obj, fn):
    me = obj.data
    for v in me.vertices:
        v.co = fn(v.co)
    me.update()
    return obj


def smooth(obj, angle=0.7):
    activate(obj)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=angle)
    except Exception:
        bpy.ops.object.shade_smooth()


def half(obj, front, cut=0.0):
    """Deja la mitad delantera (y<cut) o trasera (y>=cut) de la malla y la tapa."""
    if front:
        keep_faces(obj, lambda c: c.y < cut)
    else:
        keep_faces(obj, lambda c: c.y >= cut)
    return cap(obj)


def register(name, obj, color, smooth_angle=0.7):
    obj.name = name
    smooth(obj, smooth_angle)
    mat = bpy.data.materials.new(name + "_mat")
    mat.diffuse_color = color
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    LIB[name] = (obj, color)
    return obj


def export_fbx(obj, filename):
    os.makedirs(OUT, exist_ok=True)
    activate(obj)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, filename + ".fbx"),
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
    )


# =========================================================================== cabeza

EYE_X, EYE_Y, EYE_Z = 0.082, -0.272, 0.02
C_TENDON = (0.95, 0.85, 0.8, 1)


def ellipsoid(loc, scale, seg=20, rings=10):
    return sphere(1.0, loc=loc, scale=scale, seg=seg, rings=rings)


def capsule_between(a, b, r, verts=12):
    """Cilindro con extremos redondeados entre dos puntos (clavicula, dedo, etc.)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    c = cylinder(r, d.length, verts=verts)
    c.rotation_mode = "QUATERNION"
    c.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    c.location = (a + b) / 2
    apply_all(c)
    ends = [sphere(r, loc=tuple(p), seg=verts, rings=max(4, verts // 2)) for p in (a, b)]
    return join([c] + ends)


def build_head():
    # --- Armadura: casco con cresta, ventilaciones; visera con tornillos; mentonera con rejillas.
    shell = sphere(0.285, scale=(1.0, 1.02, 0.98), seg=48, rings=24)
    keep_faces(shell, lambda c: not (c.y < -0.08 and -0.24 < c.z < 0.13))  # abertura de la cara
    solidify(shell, 0.022)
    crest = torus(0.292, 0.02, rot=(0, math.radians(90), 0), seg=48, mseg=8)
    keep_faces(crest, lambda c: c.z > 0.02 and c.y > -0.2)
    vents = [cube((0.03, 0.07, 0.02), loc=(x, -0.17, 0.225), rot=(math.radians(-38), 0, 0), bevel=0.008, seg=2)
             for x in (-0.07, 0.07)]
    lip = torus(0.27, 0.016, loc=(0, 0.02, -0.17), scale=(1.0, 1.0, 0.6), seg=48, mseg=6)
    keep_faces(lip, lambda c: c.y > -0.05)
    register("helmet_shell", join([shell, crest, lip] + vents), C_ARMOR)

    visor = cube((0.44, 0.24, 0.026), bevel=0.01)
    ridge = cube((0.05, 0.22, 0.04), loc=(0, 0.0, 0.01), bevel=0.012)
    screws = [cylinder(0.016, 0.03, loc=(x, 0.07, 0.015), verts=12) for x in (-0.17, 0.17)]
    visor = join([visor, ridge] + screws)
    visor.rotation_euler = (math.radians(-18), 0, 0)
    visor.location = (0, -0.235, 0.17)
    apply_all(visor)
    register("helmet_visor", visor, C_ACCENT)

    chin = sphere(0.27, loc=(0, -0.04, -0.1), scale=(0.78, 1.0, 0.46), seg=40, rings=20)
    keep_faces(chin, lambda c: c.y < -0.15 and c.z < -0.06)
    solidify(chin, 0.03)
    register("helmet_chin", chin, C_ACCENT)

    parts = []
    for sx in (-1, 1):
        parts.append(torus(0.066, 0.018, loc=(sx * EYE_X, -0.262, EYE_Z), rot=(math.radians(90), 0, 0), seg=24, mseg=8))
    parts.append(cube((0.05, 0.03, 0.03), loc=(0, -0.275, EYE_Z), bevel=0.008))
    strap = torus(0.29, 0.016, loc=(0, 0, EYE_Z), seg=48, mseg=8)
    keep_faces(strap, lambda c: c.y > -0.17)
    parts.append(strap)
    for x in (-0.05, 0.0, 0.05):  # rejillas de la mentonera
        parts.append(cube((0.025, 0.03, 0.05), loc=(x, -0.288, -0.15), rot=(math.radians(-12), 0, 0), bevel=0.006, seg=2))
    register("helmet_goggles", join(parts), C_DARK)

    # --- Piel: cara con nariz, orejas, mejillas, labios y menton; cejas aparte (color del pelo).
    face = sphere(0.262, seg=40, rings=20)
    half(face, front=True)
    nose = sphere(0.045, loc=(0, -0.262, -0.04), scale=(0.8, 0.9, 1.0), seg=16, rings=8)
    ears = [sphere(0.05, loc=(sx * 0.255, 0.0, 0.0), scale=(0.45, 0.7, 1.1), seg=16, rings=8) for sx in (-1, 1)]
    cheeks = [ellipsoid((sx * 0.13, -0.205, -0.06), (0.07, 0.035, 0.055), 16, 8) for sx in (-1, 1)]
    lips = torus(0.04, 0.013, loc=(0, -0.252, -0.118), rot=(math.radians(90), 0, 0), scale=(1.0, 1.0, 0.55), seg=20, mseg=8)
    chinb = ellipsoid((0, -0.232, -0.175), (0.05, 0.03, 0.035), 16, 8)
    register("head_skin_front", join([face, nose, lips, chinb] + ears + cheeks), C_SKIN)

    brows = []
    for sx in (-1, 1):
        b = cube((0.085, 0.02, 0.02), loc=(sx * 0.085, -0.262, 0.105), rot=(0, math.radians(sx * 10), 0), bevel=0.008, seg=2)
        brows.append(b)
    register("head_brows", join(brows), C_HAIR)

    scalp = sphere(0.262, seg=40, rings=20)
    half(scalp, front=False)
    register("head_skin_back", scalp, C_SKIN)

    hair = sphere(0.272, seg=64, rings=32)
    keep_faces(hair, lambda c: c.z > 0.04 and c.y > -0.15)
    solidify(hair, 0.018)
    tufts = [ellipsoid((x, -0.08 + i * 0.05, 0.27), (0.035, 0.05, 0.03), 12, 6) for i, x in enumerate((-0.05, 0.0, 0.05))]
    register("head_hair", join([hair] + tufts), C_HAIR)

    # --- Musculo: anillos alrededor de los ojos, frente, sienes, maseteros; nuca detras.
    mf = sphere(0.25, seg=40, rings=20)
    half(mf, front=True)
    rings_ = [torus(0.068, 0.02, loc=(sx * EYE_X, -0.236, EYE_Z), rot=(math.radians(90), 0, 0), seg=20, mseg=8) for sx in (-1, 1)]
    frontal = [ellipsoid((sx * 0.06, -0.21, 0.135), (0.06, 0.017, 0.07), 16, 8) for sx in (-1, 1)]
    temporal = [ellipsoid((sx * 0.212, -0.06, 0.07), (0.028, 0.08, 0.07), 16, 8) for sx in (-1, 1)]
    masseter = [ellipsoid((sx * 0.14, -0.19, -0.1), (0.05, 0.035, 0.075), 16, 8) for sx in (-1, 1)]
    register("head_muscle_front", join([mf] + rings_ + frontal + temporal + masseter), C_MUSCLE)
    mb = sphere(0.25, seg=40, rings=20)
    half(mb, front=False)
    nape = [ellipsoid((sx * 0.07, 0.19, -0.15), (0.06, 0.04, 0.09), 16, 8) for sx in (-1, 1)]
    register("head_muscle_back", join([mb] + nape), C_MUSCLE)

    teeth = []
    for z in (-0.105, -0.14):
        for i in range(7):
            a = math.radians(-36 + i * 12)
            r = 0.238
            teeth.append(cube((0.026, 0.02, 0.03), loc=(math.sin(a) * r, -math.cos(a) * r, z), rot=(0, 0, a), bevel=0.006, seg=2))
    register("head_teeth", join(teeth), C_TEETH)

    # --- Hueso: craneo con arco superciliar, pomulos, maxilar y mandibula.
    skf = sphere(0.24, scale=(0.95, 1.0, 1.0), seg=40, rings=20)
    half(skf, front=True)
    cheekbones = [ellipsoid((sx * 0.13, -0.19, -0.04), (0.054, 0.032, 0.032), 16, 8) for sx in (-1, 1)]
    browridge = cube((0.27, 0.045, 0.035), loc=(0, -0.215, 0.098), bevel=0.016)
    maxilla = cube((0.17, 0.08, 0.05), loc=(0, -0.2, -0.085), bevel=0.02)
    mandible = cube((0.2, 0.12, 0.06), loc=(0, -0.15, -0.185), bevel=0.025)
    chin_ = ellipsoid((0, -0.21, -0.19), (0.05, 0.03, 0.035), 16, 8)
    register("skull_front", join([skf, browridge, maxilla, mandible, chin_] + cheekbones), C_BONE)

    holes = [sphere(0.062, loc=(sx * EYE_X, -0.212, EYE_Z), seg=20, rings=10) for sx in (-1, 1)]
    holes.append(ellipsoid((0, -0.232, -0.05), (0.028, 0.03, 0.04), 12, 6))
    register("skull_holes", join(holes), C_DARK)

    skb = sphere(0.24, scale=(0.95, 1.0, 1.0), seg=40, rings=20)
    half(skb, front=False)
    occ = ellipsoid((0, 0.205, -0.07), (0.08, 0.035, 0.05), 16, 8)
    register("skull_back", join([skb, occ]), C_BONE)


# =========================================================================== torso

TORSO_C = (0, 0, 0.77)


def torso_base(rx, ry, rz, front):
    o = sphere(1.0, loc=TORSO_C, scale=(rx, ry, rz), seg=40, rings=24)
    return half(o, front)


def halves(objs, front):
    """Deja solo la mitad delantera o trasera de unas piezas sueltas (sin tapar)."""
    for o in objs:
        keep_faces(o, (lambda c: c.y < 0.0) if front else (lambda c: c.y >= 0.0))
    return objs


def build_torso():
    # --- Ropa (jersey) con cuello.
    for front, name in ((True, "torso_cloth_front"), (False, "torso_cloth_back")):
        base = torso_base(0.175, 0.13, 0.275, front)
        collar = torus(0.075, 0.022, loc=(0, 0, 1.02), seg=24, mseg=8)
        halves([collar], front)
        register(name, join([base, collar]), C_JERSEY)

    # --- Peto delantero: placa principal, placas de pecho, alas laterales, placa del vientre.
    pf = sphere(1.0, loc=(0, 0, 0.86), scale=(0.205, 0.158, 0.2), seg=40, rings=20)
    keep_faces(pf, lambda c: c.y < -0.06)
    solidify(pf, 0.026)
    pecs = []
    for sx in (-1, 1):
        p = sphere(1.0, loc=(sx * 0.075, -0.02, 0.9), scale=(0.085, 0.16, 0.075), seg=28, rings=14)
        keep_faces(p, lambda c: c.y < -0.12)
        solidify(p, 0.02)
        pecs.append(p)
    wings = []
    for sx in (-1, 1):
        w = sphere(1.0, loc=(sx * 0.12, 0.0, 0.79), scale=(0.08, 0.15, 0.09), seg=24, rings=12)
        keep_faces(w, lambda c: c.y < -0.04 and abs(c.x) > 0.14)
        solidify(w, 0.018)
        wings.append(w)
    belly = sphere(1.0, loc=(0, 0, 0.665), scale=(0.165, 0.142, 0.085), seg=32, rings=16)
    keep_faces(belly, lambda c: c.y < -0.07)
    solidify(belly, 0.022)
    vent = [cube((0.07, 0.02, 0.012), loc=(0, -0.175, z), bevel=0.004, seg=2) for z in (0.82, 0.845)]
    register("armor_chest_front", join([pf, belly] + pecs + wings + vent), C_ARMOR)

    # --- Peto trasero: placa con lomo, nervaduras y placa lumbar.
    pb = sphere(1.0, loc=(0, 0, 0.85), scale=(0.2, 0.155, 0.22), seg=40, rings=20)
    keep_faces(pb, lambda c: c.y > 0.06)
    solidify(pb, 0.024)
    ridge = cube((0.045, 0.035, 0.3), loc=(0, 0.16, 0.85), bevel=0.012)
    ribs = [cube((0.24, 0.02, 0.018), loc=(0, 0.15, z), bevel=0.006, seg=2) for z in (0.78, 0.86, 0.94)]
    lumbar = sphere(1.0, loc=(0, 0, 0.64), scale=(0.16, 0.14, 0.07), seg=32, rings=16)
    keep_faces(lumbar, lambda c: c.y > 0.08)
    solidify(lumbar, 0.02)
    register("armor_chest_back", join([pb, ridge, lumbar] + ribs), C_ARMOR)

    # --- Correas laterales del peto (mitad delantera / trasera).
    for front, name in ((True, "armor_straps_front"), (False, "armor_straps_back")):
        straps = [torus(1.0, 0.012, loc=(0, 0, z), scale=(0.182, 0.137, 1.0), seg=48, mseg=6) for z in (0.7, 0.92)]
        halves(straps, front)
        register(name, join(straps), C_DARK)

    # --- Piel: clavicula y pectorales suaves delante; omoplatos y columna detras.
    sf = torso_base(0.168, 0.124, 0.268, True)
    clav = [capsule_between((sx * 0.03, -0.1, 1.0), (sx * 0.16, -0.05, 0.99), 0.014) for sx in (-1, 1)]
    softpec = [ellipsoid((sx * 0.07, -0.098, 0.9), (0.065, 0.025, 0.05), 16, 8) for sx in (-1, 1)]
    register("torso_skin_front", join([sf] + clav + softpec), C_SKIN)
    sb = torso_base(0.168, 0.124, 0.268, False)
    blades = [ellipsoid((sx * 0.08, 0.1, 0.9), (0.06, 0.025, 0.075), 16, 8) for sx in (-1, 1)]
    spinebumps = [ellipsoid((sx * 0.02, 0.118, 0.77), (0.015, 0.012, 0.17), 10, 5) for sx in (-1, 1)]
    register("torso_skin_back", join([sb] + blades + spinebumps), C_SKIN)

    # --- Musculo delante: pectorales, 6 abdominales, serratos, esternocleidomastoideos.
    mf = torso_base(0.16, 0.118, 0.26, True)
    pecs = [ellipsoid((sx * 0.072, -0.1, 0.9), (0.078, 0.037, 0.062), 24, 12) for sx in (-1, 1)]
    abs_ = [cube((0.05, 0.03, 0.05), loc=(sx * 0.032, -0.108, z), bevel=0.014, seg=2) for sx in (-1, 1) for z in (0.62, 0.685, 0.75)]
    serr = [ellipsoid((sx * 0.135, -0.065, z), (0.025, 0.03, 0.018), 10, 5) for sx in (-1, 1) for z in (0.74, 0.79, 0.84)]
    scm = [capsule_between((sx * 0.02, -0.07, 1.0), (sx * 0.06, -0.03, 1.08), 0.018) for sx in (-1, 1)]
    register("torso_muscle_front", join([mf] + pecs + abs_ + serr + scm), C_MUSCLE)
    # Detras: trapecio, dorsales, erectores de la columna.
    mb = torso_base(0.16, 0.118, 0.26, False)
    traps = ellipsoid((0, 0.085, 0.96), (0.15, 0.045, 0.09), 24, 12)
    lats = [ellipsoid((sx * 0.09, 0.095, 0.8), (0.065, 0.032, 0.13), 24, 12) for sx in (-1, 1)]
    erect = [ellipsoid((sx * 0.03, 0.11, 0.7), (0.024, 0.02, 0.16), 12, 6) for sx in (-1, 1)]
    register("torso_muscle_back", join([mb, traps] + lats + erect), C_MUSCLE)

    # --- Hueso: cavidad oscura debajo.
    register("torso_cavity_front", torso_base(0.14, 0.098, 0.235, True), C_CAVITY)
    register("torso_cavity_back", torso_base(0.14, 0.098, 0.235, False), C_CAVITY)

    def ribs7(front):
        out = []
        zs = [0.64 + i * 0.054 for i in range(7)]
        for i, z in enumerate(zs):
            k = 1.0 - abs(i - 3) * 0.045
            r = torus(0.145 * k, 0.011, loc=(0, 0, z), scale=(1.12, 0.82, 1.0), seg=40, mseg=6)
            keep_faces(r, (lambda c: c.y < -0.01) if front else (lambda c: c.y > 0.01))
            out.append(r)
        return out

    sternum = cube((0.035, 0.022, 0.26), loc=(0, -0.118, 0.84), bevel=0.01)
    manubrium = cube((0.06, 0.024, 0.045), loc=(0, -0.115, 0.98), bevel=0.012)
    clavb = [capsule_between((sx * 0.03, -0.105, 1.0), (sx * 0.17, -0.04, 0.985), 0.013) for sx in (-1, 1)]
    register("torso_bone_front", join(ribs7(True) + [sternum, manubrium] + clavb), C_BONE)

    spine = []
    for i in range(11):
        z = 0.52 + i * 0.047
        spine.append(cube((0.05, 0.04, 0.03), loc=(0, 0.095, z), bevel=0.011, seg=2))
        spine.append(cube((0.016, 0.04, 0.02), loc=(0, 0.125, z - 0.008), rot=(math.radians(-25), 0, 0), bevel=0.006, seg=2))
    scap = [ellipsoid((sx * 0.085, 0.108, 0.9), (0.06, 0.018, 0.075), 16, 8) for sx in (-1, 1)]
    pelvis = [ellipsoid((sx * 0.1, 0.05, 0.55), (0.075, 0.022, 0.06), 20, 10) for sx in (-1, 1)]  # alas iliacas planas
    sacrum = ellipsoid((0, 0.09, 0.51), (0.035, 0.02, 0.05), 12, 6)
    register("torso_bone_back", join(ribs7(False) + spine + scap + pelvis + [sacrum]), C_BONE)

    # --- Hombrera de dos laminas (centrada en el origen).
    dome = sphere(0.12, scale=(1.0, 1.15, 0.7), seg=32, rings=16)
    keep_faces(dome, lambda c: c.z > -0.01)
    solidify(dome, 0.02)
    plate = sphere(0.1, loc=(0.035, 0, -0.035), scale=(0.9, 1.1, 0.6), seg=32, rings=16)
    keep_faces(plate, lambda c: c.z > -0.045)
    solidify(plate, 0.018)
    rivets = [sphere(0.012, loc=(0.0, y, 0.085), seg=8, rings=4) for y in (-0.08, 0.08)]
    register("armor_shoulder", join([dome, plate] + rivets), C_ARMOR)


# =========================================================================== extremidades

def capsule_unit(profile, verts=24, cuts=16):
    """Capsula unitaria (Z de -1 a 1, radio maximo 0.5) con un perfil de radio por altura."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=0.5, depth=1.0, end_fill_type="NOTHING")
    body = bpy.context.active_object
    activate(body)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.subdivide(number_cuts=cuts)
    bpy.ops.object.mode_set(mode="OBJECT")
    deform(body, lambda co: Vector((co.x * profile(co.z), co.y * profile(co.z), co.z)))
    caps = [sphere(0.5 * profile(z * 0.5), loc=(0, 0, z * 0.5), seg=verts, rings=12) for z in (-1, 1)]
    return join([body] + caps)


def build_limbs():
    # Ropa con arrugas y piel con forma suave.
    register("limb_cloth", capsule_unit(lambda z: 0.97 + 0.03 * math.sin(z * 22.0)), C_JERSEY, smooth_angle=1.2)
    register("limb_skin", capsule_unit(lambda z: 1.0 - 0.06 * (z + 0.5)), C_SKIN, smooth_angle=1.2)

    # Musculo: vientre muscular + biceps (delante) y triceps (detras); tendones blancos en los extremos.
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.5, depth=1.5, end_fill_type="NOTHING")
    m = bpy.context.active_object
    activate(m)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.subdivide(number_cuts=10)
    bpy.ops.object.mode_set(mode="OBJECT")

    def bulge(co):
        t = (co.z + 0.75) / 1.5
        r = 0.7 + 0.25 * math.sin(math.pi * min(1.0, t * 1.1)) ** 2
        return Vector((co.x * r, co.y * r, co.z))

    deform(m, bulge)
    bic = ellipsoid((0, -0.2, 0.2), (0.28, 0.24, 0.48), 20, 10)
    tri = ellipsoid((0, 0.2, 0.1), (0.26, 0.22, 0.45), 20, 10)
    register("limb_muscle", join([m, bic, tri]), C_MUSCLE, smooth_angle=1.2)

    tendons = []
    for z in (-1, 1):
        tendons.append(sphere(0.3, loc=(0, 0, z * 0.82), seg=20, rings=10))
        tendons.append(cylinder(0.2, 0.2, loc=(0, 0, z * 0.7), verts=16))
    register("limb_tendon", join(tendons), C_TENDON, smooth_angle=1.2)

    shaft = cylinder(0.17, 1.5, verts=16)
    crest = ellipsoid((0, -0.1, 0.0), (0.1, 0.12, 0.5), 12, 6)
    knobs = [sphere(0.27, loc=(sx * 0.12, 0, z), seg=20, rings=10) for z in (-0.8, 0.8) for sx in (-1, 1)]
    register("limb_bone", join([shaft, crest] + knobs), C_BONE, smooth_angle=1.2)


# =========================================================================== manos y pies

def build_hands():
    # Guante: manopla, punio, protector de nudillos y dedos marcados.
    mitt = sphere(0.085, scale=(1.05, 1.0, 0.85), seg=24, rings=12)
    cuff = cylinder(0.075, 0.07, loc=(0, 0.075, 0), rot=(math.radians(90), 0, 0), verts=20)
    knuck = cube((0.13, 0.04, 0.06), loc=(0, -0.055, 0.02), bevel=0.015)
    fingers = [ellipsoid((x, -0.08, -0.005), (0.02, 0.028, 0.03), 12, 6) for x in (-0.045, -0.015, 0.015, 0.045)]
    thumb = sphere(0.032, loc=(0, -0.045, 0.065), scale=(1, 1, 1.3), seg=16, rings=8)
    register("glove", join([mitt, cuff, knuck, thumb] + fingers), C_ACCENT)

    hand = sphere(0.07, scale=(1.0, 0.9, 0.75), seg=24, rings=12)
    hf = [capsule_between((x, -0.05, 0.0), (x, -0.095, -0.01), 0.013, verts=10) for x in (-0.036, -0.012, 0.012, 0.036)]
    hthumb = capsule_between((0.0, -0.03, 0.04), (0.0, -0.06, 0.07), 0.015, verts=10)
    register("hand_skin", join([hand, hthumb] + hf), C_SKIN)

    palm = cube((0.07, 0.05, 0.022), loc=(0, 0.01, 0), bevel=0.008)
    bones = []
    for x in (-0.027, -0.009, 0.009, 0.027):
        bones.append(capsule_between((x, -0.015, 0), (x, -0.045, 0), 0.007, verts=8))
        bones.append(capsule_between((x, -0.048, 0), (x, -0.07, -0.004), 0.006, verts=8))
        bones.append(sphere(0.011, loc=(x, -0.015, 0), seg=10, rings=5))
    tb = capsule_between((0, -0.005, 0.02), (0, -0.04, 0.055), 0.008, verts=8)
    register("hand_bone", join([palm, tb] + bones), C_BONE, smooth_angle=1.2)


def build_feet():
    shaft = cube((0.17, 0.2, 0.22), loc=(0, 0.035, 0.07), bevel=0.05)
    toe = cube((0.17, 0.17, 0.1), loc=(0, -0.09, -0.02), bevel=0.045)
    toecap = sphere(1.0, loc=(0, -0.14, -0.02), scale=(0.085, 0.05, 0.05), seg=20, rings=10)
    heel = cube((0.16, 0.06, 0.06), loc=(0, 0.13, -0.035), bevel=0.02)
    plates = [cube((0.03, 0.12, 0.08), loc=(sx * 0.09, 0.03, 0.09), bevel=0.012) for sx in (-1, 1)]
    register("boot", join([shaft, toe, toecap, heel] + plates), C_BOOT)
    register("boot_sole", cube((0.19, 0.34, 0.04), loc=(0, -0.03, -0.05), bevel=0.015), C_DARK)
    buckles = [cube((0.176, 0.206, 0.022), loc=(0, 0.035, z), bevel=0.008) for z in (0.06, 0.13)]
    clips = [cube((0.04, 0.03, 0.03), loc=(0.085, 0.0, z), bevel=0.006, seg=2) for z in (0.06, 0.13)]
    register("boot_buckles", join(buckles + clips), C_ACCENT)

    foot = cube((0.11, 0.24, 0.07), loc=(0, -0.04, -0.035), bevel=0.03)
    ankle = cylinder(0.045, 0.16, loc=(0, 0.03, 0.06), verts=16)
    malleoli = [sphere(0.02, loc=(sx * 0.045, 0.03, -0.005), seg=10, rings=5) for sx in (-1, 1)]
    toes = [sphere(0.02, loc=(x, -0.165, -0.04), seg=10, rings=5) for x in (-0.036, -0.012, 0.012, 0.036)]
    register("foot_skin", join([foot, ankle] + malleoli + toes), C_SKIN)

    heelb = sphere(0.035, loc=(0, 0.05, -0.04), seg=14, rings=7)
    meta = [capsule_between((x, 0.0, -0.04), (x, -0.12, -0.05), 0.009, verts=8) for x in (-0.03, -0.01, 0.01, 0.03)]
    tips = [sphere(0.013, loc=(x, -0.135, -0.05), seg=10, rings=5) for x in (-0.03, -0.01, 0.01, 0.03)]
    tib = cylinder(0.018, 0.16, loc=(0, 0.03, 0.06), verts=10)
    talus = sphere(0.028, loc=(0, 0.03, -0.02), seg=12, rings=6)
    register("foot_bone", join([heelb, tib, talus] + meta + tips), C_BONE, smooth_angle=1.2)


def build_pads():
    k = sphere(0.13, scale=(0.75, 0.5, 1.0), seg=32, rings=16)
    keep_faces(k, lambda c: c.y < -0.015)
    solidify(k, 0.02)
    kc = sphere(0.06, loc=(0, -0.06, 0.02), scale=(1, 0.5, 0.9), seg=20, rings=10)
    keep_faces(kc, lambda c: c.y < -0.04)
    solidify(kc, 0.012)
    hinges = [cylinder(0.025, 0.02, loc=(sx * 0.1, -0.01, 0.0), rot=(0, math.radians(90), 0), verts=14) for sx in (-1, 1)]
    ribsk = [cube((0.012, 0.02, 0.16), loc=(sx * 0.04, -0.07, 0.0), bevel=0.005, seg=2) for sx in (-1, 1)]
    register("knee_guard", join([k, kc] + hinges + ribsk), C_ARMOR)

    straps = [torus(0.075, 0.011, loc=(0, 0.0, z), scale=(1.0, 0.8, 1.0), seg=24, mseg=6) for z in (-0.08, 0.08)]
    for s in straps:
        keep_faces(s, lambda c: c.y > -0.03)
    register("knee_straps", join(straps), C_DARK)

    e = sphere(0.075, scale=(1.0, 0.75, 1.0), seg=24, rings=12)
    keep_faces(e, lambda c: c.y > -0.01)
    solidify(e, 0.015)
    ecap = sphere(0.035, loc=(0, 0.05, 0), scale=(1, 0.6, 1), seg=14, rings=7)
    register("elbow_guard", join([e, ecap]), C_ARMOR)


def build_probe():
    """Cubito delante (-Y) y arriba (+Z): el juego lo usa para comprobar la orientacion de los ejes."""
    register("axis_probe", cube((0.1, 0.1, 0.1), loc=(0, -1.0, 0.5)), C_DARK)


# =========================================================================== vista previa

def preview():
    """Cuatro personajes uno al lado del otro: armadura, piel, musculo y hueso."""
    levels = [
        ["helmet_shell", "helmet_visor", "helmet_chin", "helmet_goggles", "head_skin_front",
         "torso_cloth_front", "torso_cloth_back", "armor_chest_front", "armor_chest_back",
         "armor_straps_front", "armor_straps_back",
         "armor_shoulder", "glove", "boot", "boot_sole", "boot_buckles", "knee_guard", "knee_straps"],
        ["head_skin_front", "head_skin_back", "head_hair", "head_brows", "torso_skin_front", "torso_skin_back",
         "hand_skin", "foot_skin"],
        ["head_muscle_front", "head_muscle_back", "head_teeth", "torso_muscle_front", "torso_muscle_back",
         "hand_skin:muscle", "foot_skin:muscle", "limb_muscle"],
        ["skull_front", "skull_back", "skull_holes", "head_teeth", "torso_cavity_front", "torso_cavity_back",
         "torso_bone_front", "torso_bone_back", "hand_bone", "foot_bone", "limb_bone"],
    ]
    limb_colors = [C_JERSEY, C_SKIN, C_MUSCLE, C_BONE]
    head_c = Vector((0, 0, 1.17))

    def place(name, offset, color_override=None):
        src, color = LIB[name]
        o = src.copy()
        o.data = src.data.copy()
        bpy.context.collection.objects.link(o)
        if color_override:
            mat = bpy.data.materials.new(name + "_ov")
            mat.diffuse_color = color_override
            o.data.materials.clear()
            o.data.materials.append(mat)
        o.location = offset
        return o

    def seg(name, a, b, thick, offset, color):
        d = b - a
        if name == "capsule":
            bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.5, depth=2.0)
            o = bpy.context.active_object
            mat = bpy.data.materials.new("seg")
            mat.diffuse_color = color
            o.data.materials.append(mat)
        else:
            o = place(name, (0, 0, 0))
        o.scale = (thick, thick, d.length / 2)
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
        o.location = offset + (a + b) / 2
        return o

    for lv, names in enumerate(levels):
        off = Vector((lv * 1.1, 0, 0))
        for n in names:
            base, _, tag = n.partition(":")
            if base.startswith("limb_"):
                continue
            col = C_MUSCLE if tag == "muscle" else None
            if base.startswith(("helmet", "head", "skull")):
                place(base, off + head_c, col)
            elif base in ("glove", "hand_skin", "hand_bone"):
                for sx in (-1, 1):
                    place(base, off + Vector((sx * 0.36, -0.04, 0.62)), col)
            elif base.startswith(("boot", "foot")):
                for sx in (-1, 1):
                    place(base, off + Vector((sx * 0.15, 0, 0.07)), col)
            elif base in ("knee_guard", "knee_straps"):
                for sx in (-1, 1):
                    place(base, off + Vector((sx * 0.13, -0.06, 0.3)), col)
            elif base == "armor_shoulder":
                for sx in (-1, 1):
                    o = place(base, off + Vector((sx * 0.22, 0, 0.96)), col)
                    o.scale = (sx, 1, 1)
            else:
                place(base, off, col)
        limb = ("limb_cloth", "limb_skin", "limb_muscle", "limb_bone")[lv]
        thick_mul = 1.0 if lv < 3 else 0.6
        for sx in (-1, 1):
            sh, el, ha = Vector((sx * 0.21, 0, 0.93)), Vector((sx * 0.3, 0.02, 0.76)), Vector((sx * 0.36, -0.04, 0.62))
            hip, kn, ft = Vector((sx * 0.1, 0, 0.47)), Vector((sx * 0.13, -0.05, 0.3)), Vector((sx * 0.15, 0, 0.21))
            c = limb_colors[lv]
            seg(limb, sh, el, 0.12 * thick_mul, off, c)
            seg(limb, el, ha, 0.11 * thick_mul, off, c)
            seg(limb, hip, kn, 0.14 * thick_mul, off, None)
            seg(limb, kn, ft, 0.12 * thick_mul, off, None)
            if lv == 2:
                for a_, b_, th in ((sh, el, 0.12), (el, ha, 0.11), (hip, kn, 0.14), (kn, ft, 0.12)):
                    seg("limb_tendon", a_, b_, th * 0.9, off, None)

    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 1800
    scene.render.resolution_y = 900
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 40
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    # De frente y de espaldas.
    for tag, loc, rot in (("front", (1.65, -6.2, 0.95), (math.radians(87), 0, 0)),
                          ("back", (1.65, 6.2, 0.95), (math.radians(87), 0, math.radians(180)))):
        cam.location = loc
        cam.rotation_euler = rot
        scene.render.filepath = os.path.join(HERE, "preview_body_" + tag + ".png")
        bpy.ops.render.render(write_still=True)


# =========================================================================== main

def main():
    reset_scene()
    build_head()
    build_torso()
    build_limbs()
    build_hands()
    build_feet()
    build_pads()
    build_probe()

    for name, (obj, _) in LIB.items():
        export_fbx(obj, name)
        print("[LilBombards] Exportado", name, len(obj.data.vertices), "vertices")

    # Library a un lado para no estorbar la vista previa.
    for name, (obj, _) in LIB.items():
        obj.location = (0, 0, -100)
    preview()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "lilbombards_body.blend"))
    print("[LilBombards] Listo:", len(LIB), "piezas")


if __name__ == "__main__":
    main()
