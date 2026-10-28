"""Builds the wrestler base characters from MakeHuman (MPFB2) CC0 assets and exports them for Unity.

For each gender this creates one athletic base character with the Unity-friendly Mixamo rig
(fingers, jaw and eyes included), eyes, eyebrows, eyelashes, cropped hair and wrestling shoes,
and adds shape keys that the game blends per wrestler: build (weight, muscle) and face identity
(head shape, nose, chin, jaw, cheekbones, ears). All skin textures of the chosen skins are
copied next to the model; every base shares one UV layout, so skins are interchangeable.

Run (Blender 4.2+ with MPFB2 and the makehuman_system_assets pack installed):
    blender --background --python tools/characters/build_wrestlers.py -- OUTPUT_DIR [--preview DIR]
"""
import argparse
import importlib
import json
import math
import os
import shutil
import sys

import bpy
import mathutils
import numpy as np


def mpfb(module, name):
    """MPFB is an extension, so its package path is only known at run time."""
    for loaded in list(sys.modules):
        if loaded.endswith(module):
            return getattr(importlib.import_module(loaded), name)
    raise ImportError(f"MPFB module {module} is not loaded; is the MPFB extension installed and enabled?")


HumanService = mpfb("mpfb.services.humanservice", "HumanService")
TargetService = mpfb("mpfb.services.targetservice", "TargetService")
AssetService = mpfb("mpfb.services.assetservice", "AssetService")
ExportService = mpfb("mpfb.services.exportservice", "ExportService")
ObjectService = mpfb("mpfb.services.objectservice", "ObjectService")
LocationService = mpfb("mpfb.services.locationservice", "LocationService")

RIG = "mixamo_unity"

# Base phenotypes: young adult athletes (MPFB macro values are 0-1; age 0.5 is 25 years).
BASES = {
    "male": {
        "macro": {"gender": 1.0, "age": 0.5, "muscle": 0.95, "weight": 0.64, "proportions": 0.85, "height": 0.5,
                  "cupsize": 0.5, "firmness": 0.5, "race": {"asian": 0.33, "caucasian": 0.34, "african": 0.33}},
        "skins": ["young_caucasian_male", "young_african_male", "young_asian_male", "middleage_caucasian_male",
                  "mindfront_skin_male_african_middleage", "mindfront_aksel_skin", "toigo_light_skin_male_bronze"],
        "eyebrows": "eyebrow001",
        "hair": "short01",
        "physique": 1.0,
    },
    "female": {
        "macro": {"gender": 0.0, "age": 0.5, "muscle": 0.85, "weight": 0.56, "proportions": 0.85, "height": 0.5,
                  "cupsize": 0.35, "firmness": 0.7, "race": {"asian": 0.33, "caucasian": 0.34, "african": 0.33}},
        "skins": ["young_caucasian_female", "young_african_female", "young_asian_female", "middleage_caucasian_female",
                  "callharvey3d_midtoned_female", "toigo_light_skin_female_bronze"],
        "eyebrows": "eyebrow010",
        "hair": "ponytail01",
        "physique": 0.6,
    },
}

# Sneakers with a high sock; tinted one colour in the game they read as high-top wrestling shoes.
SHOES = "shoes05"
EYES = "low-poly"
EYELASHES = "eyelashes01"

# A wrestler's build on top of the phenotype: thick neck and trapezius, wide back, strong arms
# and legs. Weights are scaled by each base's "physique" (lighter for women).
WRESTLER_PHYSIQUE = [
    ("neck/measure-neck-circ-incr", 0.8), ("neck/neck-scale-horiz-incr", 0.4),
    ("torso/torso-muscle-dorsi-incr", 0.6), ("torso/torso-muscle-pectoral-incr", 0.5),
    ("torso/torso-vshape-incr", 0.4), ("torso/measure-shoulder-dist-incr", 0.35),
    ("arms/l-upperarm-muscle-incr", 0.6), ("arms/r-upperarm-muscle-incr", 0.6),
    ("arms/l-upperarm-shoulder-muscle-incr", 0.6), ("arms/r-upperarm-shoulder-muscle-incr", 0.6),
    ("arms/l-lowerarm-muscle-incr", 0.6), ("arms/r-lowerarm-muscle-incr", 0.6),
    ("legs/l-upperleg-muscle-incr", 0.6), ("legs/r-upperleg-muscle-incr", 0.6),
    ("legs/l-lowerleg-muscle-incr", 0.4), ("legs/r-lowerleg-muscle-incr", 0.4),
]

# Shape keys the game blends per wrestler. Macro keys change the phenotype; target keys load
# MPFB modelling targets (paths relative to MPFB's data/targets) at the given weights.
BUILD_KEYS = {
    "Build_Heavy": {"weight": 1.0},
    "Build_Light": {"weight": 0.2},
    "Build_Muscular": {"muscle": 1.0},
    "Build_Soft": {"muscle": 0.35},
}
FACE_KEYS = {
    "Face_Square": [("head/head-square", 1.0)],
    "Face_Round": [("head/head-round", 1.0)],
    "Face_Oval": [("head/head-oval", 1.0)],
    "Nose_Large": [("nose/nose-volume-incr", 1.0), ("nose/nose-scale-horiz-incr", 0.5)],
    "Nose_Hump": [("nose/nose-hump-incr", 1.0)],
    "Nose_Broken": [("nose/nose-compression-compress", 0.8), ("nose/nose-trans-in", 0.4)],
    "Chin_Prominent": [("chin/chin-prominent-incr", 1.0)],
    "Jaw_Wide": [("chin/chin-width-incr", 1.0), ("chin/chin-bones-incr", 0.6)],
    "Cheekbones_High": [("cheek/l-cheek-bones-incr", 1.0), ("cheek/r-cheek-bones-incr", 1.0)],
    "Ears_Out": [("ears/l-ear-wing-incr", 1.0), ("ears/r-ear-wing-incr", 1.0)],
}


def coordinates(obj):
    values = np.empty(len(obj.data.vertices) * 3, dtype=np.float32)
    obj.data.vertices.foreach_get("co", values)
    return values


def human_coordinates(macro, targets=()):
    """Vertex positions of a human with the given phenotype and extra targets, all baked in."""
    human = HumanService.create_human(macro_detail_dict=macro)
    targets_root = LocationService.get_mpfb_data("targets")
    for fragment, weight in targets:
        TargetService.load_target(human, os.path.join(targets_root, fragment + ".target.gz"), weight=weight)
    TargetService.bake_targets(human)
    result = coordinates(human)
    bpy.data.objects.remove(human, do_unlink=True)
    return result


def find(fragment, subdir):
    path = AssetService.find_asset_absolute_path(fragment, asset_subdir=subdir)
    if path is None:
        raise RuntimeError(f"MakeHuman asset '{fragment}' ({subdir}) not found; install makehuman_system_assets.")
    return path


def mhmat_textures(mhmat_path):
    """Texture files referenced by a .mhmat skin, by role."""
    textures = {}
    folder = os.path.dirname(mhmat_path)
    with open(mhmat_path, encoding="utf-8") as handle:
        for line in handle:
            parts = line.strip().split(None, 1)
            if len(parts) == 2 and parts[0].endswith("Texture"):
                textures[parts[0]] = os.path.normpath(os.path.join(folder, parts[1].strip()))
    return textures


def material_textures(obj):
    """Image textures used by an object's materials, by the shader input they feed."""
    found = {}
    for slot in obj.material_slots:
        material = slot.material
        if material is None or not material.use_nodes:
            continue
        for link in material.node_tree.links:
            node = link.from_node
            if node.type == "NORMAL_MAP":
                image_links = [l for l in material.node_tree.links if l.to_node == node and l.from_node.type == "TEX_IMAGE"]
                if image_links:
                    found["normal"] = bpy.path.abspath(image_links[0].from_node.image.filepath)
                continue
            if node.type != "TEX_IMAGE" or node.image is None:
                continue
            role = {"Base Color": "baseColor", "Alpha": "alpha", "Roughness": "roughness", "Metallic": "metallic"}.get(link.to_socket.name)
            if role is not None:
                found.setdefault(role, bpy.path.abspath(node.image.filepath))
    return found


def build(name, spec, output, preview):
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    macro = spec["macro"]
    physique = [(fragment, weight * spec["physique"]) for fragment, weight in WRESTLER_PHYSIQUE]
    variants = {}
    for key, overrides in BUILD_KEYS.items():
        variant_macro = json.loads(json.dumps(macro))
        variant_macro.update(overrides)
        variants[key] = human_coordinates(variant_macro, physique)
    for key, targets in FACE_KEYS.items():
        variants[key] = human_coordinates(macro, physique + targets)

    basemesh = HumanService.create_human(macro_detail_dict=macro)
    targets_root = LocationService.get_mpfb_data("targets")
    for fragment, weight in physique:
        TargetService.load_target(basemesh, os.path.join(targets_root, fragment + ".target.gz"), weight=weight)
    TargetService.bake_targets(basemesh)
    basis = coordinates(basemesh)
    skin_paths = {skin: find(skin + ".mhmat", "skins") for skin in spec["skins"]}
    HumanService.set_character_skin(skin_paths[spec["skins"][0]], basemesh, skin_type="GAMEENGINE")
    HumanService.add_builtin_rig(basemesh, RIG)
    for subdir, fragment, kind in (
            ("eyes", EYES + ".mhclo", "Eyes"),
            ("eyebrows", spec["eyebrows"] + ".mhclo", "Eyebrows"),
            ("eyelashes", EYELASHES + ".mhclo", "Eyelashes"),
            ("hair", spec["hair"] + ".mhclo", "Hair"),
            ("clothes", SHOES + ".mhclo", "Clothes")):
        HumanService.add_mhclo_asset(find(fragment, subdir), basemesh, asset_type=kind, material_type="GAMEENGINE")

    basemesh.shape_key_add(name="Basis", from_mix=False)
    for key, values in variants.items():
        if values.shape != basis.shape:
            raise RuntimeError(f"{key}: vertex count differs from the base mesh.")
        block = basemesh.shape_key_add(name=key, from_mix=False)
        block.data.foreach_set("co", values)
        block.value = 0.0

    export_root = ExportService.create_character_copy(basemesh, name_suffix="")
    export_mesh = ObjectService.find_object_of_type_amongst_nearest_relatives(export_root, "Basemesh")
    ExportService.bake_modifiers_remove_helpers(export_mesh, bake_masks=True, bake_subdiv=False, remove_helpers=True, also_proxy=True)
    bpy.data.objects.remove(basemesh, do_unlink=True)

    models = os.path.join(output, "Models")
    textures = os.path.join(output, "Textures", name)
    os.makedirs(models, exist_ok=True)
    os.makedirs(textures, exist_ok=True)
    skins = {}
    for skin, path in skin_paths.items():
        skins[skin] = {}
        for role, texture in mhmat_textures(path).items():
            target = os.path.join(textures, os.path.basename(texture))
            shutil.copy2(texture, target)
            skins[skin][role] = os.path.basename(texture)

    parts = []
    for child in ObjectService.get_list_of_children(export_root):
        if child.type != "MESH" or child == export_mesh:
            continue
        files = {}
        for role, texture in material_textures(child).items():
            shutil.copy2(texture, os.path.join(textures, os.path.basename(texture)))
            files[role] = os.path.basename(texture)
        parts.append({"object": child.name, "kind": ObjectService.get_object_type(child), "textures": files})

    bpy.ops.object.select_all(action="DESELECT")
    export_root.select_set(True)
    for child in ObjectService.get_list_of_children(export_root):
        child.select_set(True)
    bpy.context.view_layer.objects.active = export_root
    fbx = os.path.join(models, f"wrestler_{name}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx,
        use_selection=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=False,
        mesh_smooth_type="FACE",
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=True,
        bake_anim=False,
        path_mode="STRIP",
        embed_textures=False)

    meta = {
        "schemaVersion": 1,
        "base": name,
        "rig": RIG,
        "model": os.path.relpath(fbx, output),
        "shapeKeys": list(variants.keys()),
        "body": export_mesh.name,
        "skins": skins,
        "parts": parts,
        "source": "MakeHuman / MPFB2 makehuman_system_assets (CC0)",
    }
    with open(os.path.join(output, f"wrestler_{name}.json"), "w", encoding="utf-8") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")
    print(f"Wrote {fbx}")

    if preview:
        render_preview(name, preview)


def render_preview(name, folder):
    """Front and three-quarter renders for review."""
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items} else "BLENDER_EEVEE"
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 1600
    world = bpy.data.worlds.new("Preview") if scene.world is None else scene.world
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.055, 0.065, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sun = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(55), math.radians(5), math.radians(-25))
    scene.collection.objects.link(sun)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
    fill.data.energy = 1.2
    fill.rotation_euler = (math.radians(60), 0, math.radians(-120))
    scene.collection.objects.link(fill)
    camera = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    corners = [obj.matrix_world @ mathutils.Vector(corner) for obj in scene.objects if obj.type == "MESH" for corner in obj.bound_box]
    low = mathutils.Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    high = mathutils.Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    height = high.z - low.z
    centre = (low + high) * 0.5
    # MakeHuman characters face -Y in Blender.
    for label, offset, aim, lens in (
            ("body", mathutils.Vector((0.0, -2.6, 0.0)) * height, centre, 50),
            ("three-quarter", mathutils.Vector((1.5, -1.9, 0.1)) * height, centre, 50),
            ("face", mathutils.Vector((0.12, -0.55, 0.0)) * height, mathutils.Vector((centre.x, centre.y, low.z + 0.93 * height)), 85)):
        camera.location = aim + offset
        camera.data.lens = lens
        camera.rotation_euler = (aim - camera.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(folder, f"{name}-{label}.png")
        bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--preview")
    parser.add_argument("--only", choices=sorted(BASES))
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    for name, spec in BASES.items():
        if args.only and name != args.only:
            continue
        build(name, spec, args.output, args.preview)


main()
