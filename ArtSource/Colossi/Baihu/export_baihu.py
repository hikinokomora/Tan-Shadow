# Экспорт Байху из Baihu.blend в Unity: SK_Baihu.fbx (меш + риг + 4 клипа) и текстуры HDRP Lit.
# Запуск (из корня репозитория):
#   Blender -b ArtSource/Colossi/Baihu/Baihu.blend --python ArtSource/Colossi/Baihu/export_baihu.py
# Затем в Unity: ShadowTan → Setup → Art → Autumn Baihu Model.
import os
import bpy
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(bpy.data.filepath), "..", "..", ".."))
ART = os.path.join(ROOT, "Assets", "_Project", "Art", "Colossi", "Baihu")
MODELS = os.path.join(ART, "Models")
TEXTURES = os.path.join(ART, "Textures")

RIG = "Tiger Rig"
BODY = "output_unwrapped"
CLIPS = ["Baihu_AttackJump", "Baihu_LandMiss", "Baihu_MonumentHit", "Baihu_ReturnToBamboo"]


def pixels(image):
    data = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(data)
    return data.reshape(image.size[1], image.size[0], 4)


def save(name, rgba, colorspace):
    h, w, _ = rgba.shape
    image = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    image.colorspace_settings.name = colorspace
    image.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    image.filepath_raw = os.path.join(TEXTURES, name + ".png")
    image.file_format = "PNG"
    image.save()
    bpy.data.images.remove(image)


def export_textures(material):
    images = {n.image.name.split(".")[0]: n.image for n in material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
    base, mr, normal = images["texture_0"], images["texture_0_metallic_roughness"], images["normal"]
    albedo = pixels(base)
    save("T_Baihu_BaseColor", albedo, "sRGB")
    save("T_Baihu_Normal", pixels(normal), "Non-Color")
    # Свечение — только красные глаза (GDD-AU §06): насыщенно-красные пиксели атласа, остальное чёрное.
    r, g, b = albedo[..., 0], albedo[..., 1], albedo[..., 2]
    eyes = ((r > 0.35) & (g < 0.22) & (b < 0.22) & (r > 2.2 * np.maximum(g, b))).astype(np.float32)
    emission = np.zeros_like(albedo)
    emission[..., 0] = eyes
    emission[..., 1] = eyes * 0.06
    emission[..., 2] = eyes * 0.03
    emission[..., 3] = 1.0
    save("T_Baihu_Emission", emission, "sRGB")
    print("Baihu eye texels:", int(eyes.sum()))
    # glTF metallic-roughness: G — шероховатость, B — металличность. Маска HDRP: R металл, G AO, B деталь, A гладкость.
    src = pixels(mr)
    mask = np.empty_like(src)
    mask[..., 0] = src[..., 2]
    mask[..., 1] = 1.0
    mask[..., 2] = 0.0
    mask[..., 3] = 1.0 - src[..., 1]
    save("T_Baihu_Mask", mask, "Non-Color")


def main():
    os.makedirs(MODELS, exist_ok=True)
    os.makedirs(TEXTURES, exist_ok=True)
    rig, body = bpy.data.objects[RIG], bpy.data.objects[BODY]
    material = body.material_slots[0].material
    export_textures(material)

    material.name = "M_Baihu_Fur"
    body.name = "SK_Baihu_Body"
    # Позу привязки пишем без активного клипа; клипы уходят отдельными дублями из списка действий.
    rig.animation_data.action = None
    for action in bpy.data.actions:
        if action.name not in CLIPS:
            action.use_fake_user = False

    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    for o in bpy.context.view_layer.objects:
        o.select_set(o in (rig, body))
    bpy.context.view_layer.objects.active = rig

    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODELS, "SK_Baihu.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        use_mesh_modifiers=False,
        mesh_smooth_type="FACE",
        use_tspace=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
        embed_textures=False,
    )
    print("Baihu export done:", MODELS, TEXTURES)


main()
