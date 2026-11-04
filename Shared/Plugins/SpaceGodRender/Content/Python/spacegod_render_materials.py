"""Builds SpaceGodRender's materials: Unity.SpaceGod's twelve shaders as Unreal materials (US-T24).

Each material is unlit and does its work in Custom expression nodes that #include the plugin's shader files
(/Plugin/SpaceGodRender/*.ush, mapped by the SpaceGodRender module at PostConfigInit). Every Unity material property
becomes a material parameter of the same name (_Color, _MainTex, ...); Unity's shader globals (_SunPositionWS,
_SunColor, ...) become the Material Parameter Collection MPC_SpaceGod. See ../../README.md for the full mapping.

Run headless (from the repo root; any project that enables the plugin works):
    UnrealEditor-Cmd.exe CSharp/UnrealSpaceGodCSharp.uproject -run=pythonscript
        -script="<repo>/Shared/Plugins/SpaceGodRender/Content/Python/spacegod_render_materials.py"
        -unattended -nop4 -nosplash -NullRHI
It rebuilds every material graph and the collection in place (the assets keep their paths and references), so rerun
it after changing this file, then commit the .uasset files. spacegod_render_verify.py compiles them and reports errors.
"""

import os
import struct
import tempfile
import zlib

import unreal

ROOT = "/SpaceGodRender"
MATERIALS = ROOT + "/Materials"
TEXTURES = ROOT + "/Textures"
MPC_NAME = "MPC_SpaceGod"
SHADERS = "/Plugin/SpaceGodRender"

mel = unreal.MaterialEditingLibrary
eal = unreal.EditorAssetLibrary
tools = unreal.AssetToolsHelpers.get_asset_tools()


# --------------------------------------------------------------------------------------------------------------------
# Colour: Unity's linear-space SetColor (and a Color property's default) converts sRGB -> linear; SetVector does not.

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def unity_color(r, g, b, a=1.0):
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), a)


# --------------------------------------------------------------------------------------------------------------------
# Default textures: Unity's "white", "black", "grey" and "bump", with exact values.

def _png_bytes(rgba, size=4):
    raw = b"".join(b"\x00" + bytes(rgba) * size for _ in range(size))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


DEFAULT_TEXTURES = {
    # name: (rgba bytes, sRGB)
    "T_SG_White": ((255, 255, 255, 255), True),
    "T_SG_Black": ((0, 0, 0, 255), True),
    "T_SG_Grey": ((128, 128, 128, 128), False),
    "T_SG_Bump": ((128, 128, 255, 255), False),
}


def import_default_textures():
    folder = tempfile.mkdtemp(prefix="spacegod_render_")
    tasks = []
    for name, (rgba, _) in DEFAULT_TEXTURES.items():
        path = os.path.join(folder, name + ".png")
        with open(path, "wb") as f:
            f.write(_png_bytes(rgba))
        task = unreal.AssetImportTask()
        task.set_editor_property("filename", path)
        task.set_editor_property("destination_path", TEXTURES)
        task.set_editor_property("automated", True)
        task.set_editor_property("save", False)
        task.set_editor_property("replace_existing", True)
        tasks.append(task)
    tools.import_asset_tasks(tasks)
    for name, (_, srgb) in DEFAULT_TEXTURES.items():
        texture = unreal.load_asset(f"{TEXTURES}/{name}")
        texture.set_editor_property("srgb", srgb)
        texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_VECTOR_DISPLACEMENTMAP)
        texture.set_editor_property("mip_gen_settings", unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS)
        texture.set_editor_property("filter", unreal.TextureFilter.TF_NEAREST)


def default_texture(name):
    return unreal.load_asset(f"{TEXTURES}/{name}")


# --------------------------------------------------------------------------------------------------------------------
# The Material Parameter Collection: Unity's shader globals (Shader.SetGlobal*), names kept.

MPC_VECTORS = [
    ("_SunPositionWS", (0.0, 0.0, 0.0, 1.0)),   # world position of the main star (Unreal world, not camera-relative)
    ("_SunColor", (1.0, 1.0, 1.0, 1.0)),        # its light colour times intensity (SetGlobalVector: no sRGB conversion)
    ("_Sun2PositionWS", (0.0, 0.0, 0.0, 1.0)),  # a second star (Alpha Centauri B), black where there is none
    ("_Sun2Color", (0.0, 0.0, 0.0, 1.0)),
]
MPC_SCALARS = [
    ("_UnitySpaceSceneLuminance", 0.0),  # > 0: the HDRP-style physical exposure path (RenderGlobals.SceneLuminance)
    ("_UnitySpaceSunBrightness", 0.0),   # the star's glare (RenderGlobals.SunBrightness); <= 0 means 1
]


def build_collection():
    mpc = get_or_create(MPC_NAME, unreal.MaterialParameterCollection, unreal.MaterialParameterCollectionFactoryNew())
    scalars = []
    for name, value in MPC_SCALARS:
        p = unreal.CollectionScalarParameter()
        p.set_editor_property("parameter_name", name)
        p.set_editor_property("default_value", value)
        scalars.append(p)
    vectors = []
    for name, value in MPC_VECTORS:
        p = unreal.CollectionVectorParameter()
        p.set_editor_property("parameter_name", name)
        p.set_editor_property("default_value", unreal.LinearColor(*value))
        vectors.append(p)
    mpc.set_editor_property("scalar_parameters", scalars)
    mpc.set_editor_property("vector_parameters", vectors)
    return mpc


# --------------------------------------------------------------------------------------------------------------------
# Graph building.

class Graph:
    """One material's node graph: parameter nodes are made once per name and reused."""

    def __init__(self, mat, mpc):
        self.mat = mat
        self.mpc = mpc
        self.params = {}
        self.shared = {}
        self.y = 0

    def _new(self, cls, x=-1600):
        e = mel.create_material_expression(self.mat, cls, x, self.y)
        self.y += 90
        return e

    def mask(self, source, out, channels):
        if channels is None:
            return source, out
        m = self._new(unreal.MaterialExpressionComponentMask, -1200)
        for c in "rgba":
            m.set_editor_property(c, c in channels)
        mel.connect_material_expressions(source, out, m, "")
        return m, ""

    def subtract(self, a, b):
        s = self._new(unreal.MaterialExpressionSubtract, -1000)
        mel.connect_material_expressions(a[0], a[1], s, "A")
        mel.connect_material_expressions(b[0], b[1], s, "B")
        return s, ""

    def once(self, key, make):
        if key not in self.shared:
            self.shared[key] = make()
        return self.shared[key]

    # Parameters ------------------------------------------------------------------------------------------------
    def scalar(self, name, default):
        if name not in self.params:
            e = self._new(unreal.MaterialExpressionScalarParameter)
            e.set_editor_property("parameter_name", name)
            e.set_editor_property("default_value", float(default))
            self.params[name] = e
        return self.params[name], ""

    def vector(self, name, default):
        """The parameter as a float4 (its "RGBA" output; the default output is only RGB)."""
        if name not in self.params:
            e = self._new(unreal.MaterialExpressionVectorParameter)
            e.set_editor_property("parameter_name", name)
            e.set_editor_property("default_value", unreal.LinearColor(*default))
            self.params[name] = e
        return self.params[name], "RGBA"

    def texture(self, name, default):
        if name not in self.params:
            e = self._new(unreal.MaterialExpressionTextureObjectParameter)
            e.set_editor_property("parameter_name", name)
            e.set_editor_property("texture", default_texture(default))
            self.params[name] = e
        return self.params[name], ""

    def collection(self, name):
        def make():
            e = self._new(unreal.MaterialExpressionCollectionParameter)
            e.set_editor_property("collection", self.mpc)
            e.set_editor_property("parameter_name", name)
            return e, ""
        return self.once(("mpc", name), make)

    # Built-ins -------------------------------------------------------------------------------------------------
    def camera(self):
        return self.once("camera", lambda: (self._new(unreal.MaterialExpressionCameraPositionWS), ""))

    def position_cr(self, exclude_offsets):
        def make():
            e = self._new(unreal.MaterialExpressionWorldPosition)
            offsets = unreal.WorldPositionIncludedOffsets
            e.set_editor_property("world_position_shader_offset",
                                  offsets.WPT_CAMERA_RELATIVE_NO_OFFSETS if exclude_offsets else offsets.WPT_CAMERA_RELATIVE)
            return e, ""
        return self.once(("poscr", exclude_offsets), make)

    def position_os(self):
        def make():
            e = self._new(unreal.MaterialExpressionLocalPosition)
            try:
                e.set_editor_property("included_offsets", unreal.PositionIncludedOffsets.EXCLUDE_OFFSETS)
            except Exception as error:  # older enum spelling
                unreal.log_warning(f"SpaceGodRender: LocalPosition offsets left at default ({error})")
            return e, ""
        return self.once("posos", make)

    def uv(self, index):
        def make():
            e = self._new(unreal.MaterialExpressionTextureCoordinate)
            e.set_editor_property("coordinate_index", index)
            return e, ""
        return self.once(("uv", index), make)

    def vertex_color(self):
        def make():
            vc = self._new(unreal.MaterialExpressionVertexColor)
            a = self._new(unreal.MaterialExpressionAppendVector, -1200)
            mel.connect_material_expressions(vc, "", a, "A")
            mel.connect_material_expressions(vc, "A", a, "B")
            return a, ""
        return self.once("vc", make)

    def source(self, spec):
        """A Custom node input from its spec tuple."""
        kind = spec[0]
        if kind == "scalar":
            return self.scalar(spec[1], spec[2])
        if kind == "vector":  # ("vector", name, default, channels)
            return self.mask(*self.vector(spec[1], spec[2]), spec[3])
        if kind == "texture":
            return self.texture(spec[1], spec[2])
        if kind == "mpc":  # ("mpc", name, channels or None)
            return self.mask(*self.collection(spec[1]), spec[2])
        if kind == "mpc_rel":  # an MPC world position made camera-relative
            return self.subtract(self.mask(*self.collection(spec[1]), "rgb"), self.camera())
        if kind == "vector_rel":  # ("vector_rel", name, default): a world-position parameter made camera-relative
            return self.subtract(self.mask(*self.vector(spec[1], spec[2]), "rgb"), self.camera())
        if kind == "uv":  # ("uv", index, channels)
            return self.mask(*self.uv(spec[1]), spec[2])
        if kind == "vc":
            return self.vertex_color()
        if kind == "normal_ws":
            return self.once("nws", lambda: (self._new(unreal.MaterialExpressionVertexNormalWS), ""))
        if kind == "pos_cr":
            return self.position_cr(False)
        if kind == "pos_cr_vs":
            return self.position_cr(True)
        if kind == "pos_os":
            return self.position_os()
        if kind == "time":
            return self.once("time", lambda: (self._new(unreal.MaterialExpressionTime), ""))
        if kind == "exposure":
            return self.once("exposure", lambda: (self._new(unreal.MaterialExpressionEyeAdaptation), ""))
        if kind == "node":
            return spec[1], spec[2]
        raise ValueError(f"unknown input kind {kind}")

    def custom(self, description, include, code, output_type, inputs):
        c = mel.create_material_expression(self.mat, unreal.MaterialExpressionCustom, -500, self.y)
        self.y += 200
        c.set_editor_property("description", description)
        c.set_editor_property("code", code)
        c.set_editor_property("output_type", output_type)
        c.set_editor_property("include_file_paths", [f"{SHADERS}/{include}"])
        custom_inputs = []
        for name, _ in inputs:
            ci = unreal.CustomInput()
            ci.set_editor_property("input_name", name)
            custom_inputs.append(ci)
        c.set_editor_property("inputs", custom_inputs)
        for name, spec in inputs:
            node, out = self.source(spec)
            if not mel.connect_material_expressions(node, out, c, name):
                raise RuntimeError(f"{self.mat.get_name()}: could not connect input {name}")
        return c


F3 = unreal.CustomMaterialOutputType.CMOT_FLOAT3
F4 = unreal.CustomMaterialOutputType.CMOT_FLOAT4

# Inputs most fragments share.
LIGHTS = [
    ("SunRel", ("mpc_rel", "_SunPositionWS")),
    ("SunColor", ("mpc", "_SunColor", "rgb")),
    ("Sun2Rel", ("mpc_rel", "_Sun2PositionWS")),
    ("Sun2Color", ("mpc", "_Sun2Color", "rgb")),
]
EXPOSE = [
    ("Exposure", ("exposure",)),
    ("SceneLuminance", ("mpc", "_UnitySpaceSceneLuminance", None)),
]
VIEW_VS = [
    ("PositionCR", ("pos_cr_vs",)),
]


def scalar(name, value):
    return (name, ("scalar", name, value))


def color3(name, rgba):
    """A Unity Color property (default converted sRGB -> linear, as Unity does), used as rgb."""
    return (name, ("vector", name, unity_color(*rgba), "rgb"))


def vec4(name, value):
    """A Unity Vector property (no conversion), used whole."""
    return (name, ("vector", name, value, None))


def texture(name, default):
    return (name, ("texture", name, default))


def arg_list(inputs, skip=()):
    return ", ".join(name for name, _ in inputs if name not in skip)


# --------------------------------------------------------------------------------------------------------------------
# The materials.

def get_or_create(name, cls, factory):
    """Reuses an existing asset (so references to it survive a rebuild), else creates it."""
    path = f"{MATERIALS}/{name}"
    # load_asset, not does_asset_exist: a commandlet's asset registry may not have scanned plugin content yet.
    existing = unreal.load_asset(path)
    if existing is not None:
        return existing
    asset = tools.create_asset(name, MATERIALS, cls, factory)
    if asset is None:
        raise RuntimeError(f"could not create {path}")
    return asset


def new_material(name, blend, two_sided):
    mat = get_or_create(name, unreal.Material, unreal.MaterialFactoryNew())
    mel.delete_all_material_expressions(mat)
    mat.set_editor_property("shading_model", unreal.MaterialShadingModel.MSM_UNLIT)
    mat.set_editor_property("blend_mode", blend)
    mat.set_editor_property("two_sided", two_sided)
    for flag in ("used_with_instanced_static_meshes",):
        mat.set_editor_property(flag, True)
    if blend != unreal.BlendMode.BLEND_OPAQUE:
        # Unity's hand-written passes take no fog.
        for prop, value in (("use_translucency_vertex_fog", False), ("apply_fogging", False)):
            try:
                mat.set_editor_property(prop, value)
            except Exception:
                pass
    return mat


def finish(mat, emissive, opacity=None, wpo=None):
    mel.connect_material_property(emissive[0], emissive[1], unreal.MaterialProperty.MP_EMISSIVE_COLOR)
    if opacity is not None:
        mel.connect_material_property(opacity[0], opacity[1], unreal.MaterialProperty.MP_OPACITY)
    if wpo is not None:
        mel.connect_material_property(wpo, "", unreal.MaterialProperty.MP_WORLD_POSITION_OFFSET)
    mel.recompile_material(mat)


def build_planet(mpc):
    mat = new_material("M_Planet", unreal.BlendMode.BLEND_OPAQUE, False)
    g = Graph(mat, mpc)
    props = [
        texture("_MainTex", "T_SG_White"), texture("_NormalMap", "T_SG_Bump"),
        color3("_Color", (1, 1, 1, 1)), scalar("_UseTexture", 0), scalar("_UseNormalMap", 0),
        scalar("_NormalStrength", 1), scalar("_PoleMargin", 0), scalar("_NoiseStrength", 0.3),
        scalar("_NoiseScale", 3), scalar("_Haze", 0), color3("_HazeColor", (1, 1, 1, 1)), scalar("_CloudSpin", 0),
        color3("_AtmoColor", (0.4, 0.6, 1, 1)), scalar("_AtmoStrength", 0), scalar("_Ambient", 0.02),
        scalar("_Highlight", 0),
    ]
    inputs = [("UV", ("uv", 0, "rg")), ("NormalWS", ("normal_ws",)), ("PositionCR", ("pos_cr",)),
              ("PositionOS", ("pos_os",))] + LIGHTS + props + EXPOSE
    code = ("return SG_Expose3(SG_Planet(UV, NormalWS, PositionCR, PositionOS, SG_LOCAL_TO_WORLD_3X3, SG_INV_SCALE, "
            "SunRel, SunColor, Sun2Rel, Sun2Color, _MainTex, _MainTexSampler, _NormalMap, _NormalMapSampler, "
            "_Color, _UseTexture, _UseNormalMap, _NormalStrength, _PoleMargin, _NoiseStrength, _NoiseScale, _Haze, "
            "_HazeColor, _CloudSpin, _AtmoColor, _AtmoStrength, _Ambient, _Highlight), Exposure, SceneLuminance);")
    c = g.custom("Unity.SpaceGod/Planet", "Planet.ush", code, F3, inputs)
    finish(mat, (c, ""))
    return mat


def build_sun(mpc):
    mat = new_material("M_Sun", unreal.BlendMode.BLEND_OPAQUE, False)
    g = Graph(mat, mpc)
    inputs = [("NormalWS", ("normal_ws",)), ("PositionCR", ("pos_cr",)), ("PositionOS", ("pos_os",)),
              ("Time", ("time",)), ("SunBrightness", ("mpc", "_UnitySpaceSunBrightness", None)),
              color3("_Color", (1, 0.9, 0.7, 1)), scalar("_Intensity", 1.15)] + EXPOSE
    code = ("return SG_Expose3(SG_Sun(NormalWS, PositionCR, PositionOS, Time, SunBrightness, _Color, _Intensity), "
            "Exposure, SceneLuminance);")
    c = g.custom("Unity.SpaceGod/Sun", "Sun.ush", code, F3, inputs)
    finish(mat, (c, ""))
    return mat


def build_ring(mpc):
    mat = new_material("M_Ring", unreal.BlendMode.BLEND_TRANSLUCENT, True)
    g = Graph(mat, mpc)
    inputs = [("UV", ("uv", 0, "rg")), ("PositionCR", ("pos_cr",))] + LIGHTS + [
        color3("_Color", (0.86, 0.78, 0.64, 1)), scalar("_InnerKm", 74658), scalar("_OuterKm", 136775),
        ("PlanetRel", ("vector_rel", "_PlanetPosRadius", (0, 0, 0, 1))),
        ("PlanetRadius", ("vector", "_PlanetPosRadius", (0, 0, 0, 1), "a")),
    ] + EXPOSE
    # Unity's ring normal is object +Y, Unreal's local +Z (the annulus lies in the local XY plane).
    code = ("float3 normalWS = SG_LocalNormalToWorld(float3(0, 0, 1), SG_LOCAL_TO_WORLD_3X3, SG_INV_SCALE);\n"
            "return SG_Expose(SG_Ring(UV, PositionCR, normalWS, SunRel, SunColor, Sun2Rel, Sun2Color, _Color, "
            "_InnerKm, _OuterKm, PlanetRel, PlanetRadius), Exposure, SceneLuminance);")
    c = g.custom("Unity.SpaceGod/Ring", "Ring.ush", code, F4, inputs)
    rgb = g.mask(c, "", "rgb")
    a = g.mask(c, "", "a")
    finish(mat, rgb, opacity=a)
    return mat


def build_gas_giant(mpc):
    mat = new_material("M_GasGiant", unreal.BlendMode.BLEND_OPAQUE, False)
    g = Graph(mat, mpc)
    drift = [vec4(f"_Drift_{k}", (0, 0, 0, 0)) for k in range(10)]
    inputs = [("UV", ("uv", 0, "rg")), ("NormalWS", ("normal_ws",)), ("PositionCR", ("pos_cr",))] + LIGHTS + [
        texture("_MainTex", "T_SG_White")] + drift + [
        vec4("_Flow", (0, 0, 0, 0)), vec4("_Grs", (0, 0, 0, 0)), vec4("_GrsShape", (0, 0, 0, 0)),
        scalar("_PoleMargin", 0), color3("_AtmoColor", (0.9, 0.8, 0.65, 1)), scalar("_AtmoStrength", 0.3),
        scalar("_Ambient", 0.02), scalar("_Highlight", 0), scalar("_Turbulence", 0.35),
    ] + EXPOSE
    code = ("return SG_Expose3(SG_GasGiant(UV, NormalWS, PositionCR, SunRel, SunColor, Sun2Rel, Sun2Color, "
            "_MainTex, _MainTexSampler, " + ", ".join(f"_Drift_{k}" for k in range(10)) + ", "
            "_Flow, _Grs, _GrsShape, _PoleMargin, _AtmoColor, _AtmoStrength, _Ambient, _Highlight, _Turbulence), "
            "Exposure, SceneLuminance);")
    c = g.custom("Unity.SpaceGod/GasGiant", "GasGiant.ush", code, F3, inputs)
    finish(mat, (c, ""))
    return mat


SKY_DISTANCE = 1.0e15  # Unreal units: beyond every piece of the scene (Cpp: 1 unit = 1000 km)


def build_sky(mpc):
    mat = new_material("M_Sky", unreal.BlendMode.BLEND_OPAQUE, True)
    g = Graph(mat, mpc)
    wpo_inputs = [("LocalPos", ("pos_os",)), ("PositionCR", ("pos_cr_vs",)), scalar("_SkyDistance", SKY_DISTANCE)]
    wpo = g.custom("Unity.SpaceGod/Sky (vertex)", "Sky.ush",
                   "return SG_SkyOffset(TransformLocalVectorToWorld(Parameters, LocalPos), PositionCR, _SkyDistance);",
                   F3, wpo_inputs)
    inputs = [("LocalPos", ("pos_os",)), texture("_MainTex", "T_SG_Black"), scalar("_Brightness", 1),
              ("_AxisX", ("vector", "_AxisX", (1, 0, 0, 0), "rgb")),
              ("_AxisY", ("vector", "_AxisY", (0, -1, 0, 0), "rgb")),   # Unity (0, 0, 1) in Unreal axes
              ("_AxisZ", ("vector", "_AxisZ", (0, 0, 1, 0), "rgb"))] + EXPOSE  # Unity (0, 1, 0)
    code = ("return SG_Expose3(SG_Sky(LocalPos, _MainTex, _MainTexSampler, _Brightness, _AxisX, _AxisY, _AxisZ), "
            "Exposure, SceneLuminance);")
    c = g.custom("Unity.SpaceGod/Sky", "Sky.ush", code, F3, inputs)
    finish(mat, (c, ""), wpo=wpo)
    return mat


def build_stars(mpc):
    mat = new_material("M_Stars", unreal.BlendMode.BLEND_ADDITIVE, True)
    g = Graph(mat, mpc)
    wpo_inputs = [("LocalPos", ("pos_os",)), ("PositionCR", ("pos_cr_vs",)), ("Corner", ("uv", 0, "rg")),
                  scalar("_PointSizePx", 1), scalar("_SkyDistance", SKY_DISTANCE)]
    # Half the sky's distance, so the stars pass the depth test against the sky panorama drawn behind them.
    wpo = g.custom("Unity.SpaceGod/Stars (vertex)", "Points.ush",
                   "return SG_StarsOffset(TransformLocalVectorToWorld(Parameters, LocalPos), PositionCR, Corner, "
                   "_PointSizePx, 0.5 * _SkyDistance, SG_VIEW_ARGS);", F3, wpo_inputs)
    inputs = [("VertexColor", ("vc",)), scalar("_Brightness", 1)] + EXPOSE
    c = g.custom("Unity.SpaceGod/Stars", "Points.ush",
                 "return SG_Expose3(SG_PointColor(VertexColor, _Brightness), Exposure, SceneLuminance);", F3, inputs)
    finish(mat, (c, ""), wpo=wpo)
    return mat


def build_points(mpc):
    mat = new_material("M_Points", unreal.BlendMode.BLEND_ADDITIVE, True)
    g = Graph(mat, mpc)
    wpo_inputs = [("PositionCR", ("pos_cr_vs",)), ("Corner", ("uv", 0, "rg")), scalar("_PointSizePx", 1)]
    wpo = g.custom("Unity.SpaceGod/Points (vertex)", "Points.ush",
                   "return SG_PointOffset(PositionCR, PositionCR, Corner, _PointSizePx, SG_VIEW_ARGS);", F3, wpo_inputs)
    inputs = [("VertexColor", ("vc",)), scalar("_Brightness", 1)] + EXPOSE
    c = g.custom("Unity.SpaceGod/Points", "Points.ush",
                 "return SG_Expose3(SG_PointColor(VertexColor, _Brightness), Exposure, SceneLuminance);", F3, inputs)
    finish(mat, (c, ""), wpo=wpo)
    return mat


def build_line(mpc):
    mat = new_material("M_Line", unreal.BlendMode.BLEND_TRANSLUCENT, True)
    g = Graph(mat, mpc)
    wpo_inputs = [("LocalPos", ("pos_os",)), ("PositionCR", ("pos_cr_vs",)), ("Other", ("uv", 1, "rg")),
                  ("OtherZSide", ("uv", 2, "rg")), scalar("_LineWidthPx", 1)]
    wpo = g.custom("Unity.SpaceGod/Line (vertex)", "Points.ush",
                   "float3 otherCR = PositionCR + TransformLocalVectorToWorld(Parameters, float3(Other, OtherZSide.x) - LocalPos);\n"
                   "return SG_LineOffset(PositionCR, otherCR, OtherZSide.y, _LineWidthPx, SG_VIEW_ARGS);", F3, wpo_inputs)
    inputs = [("VertexColor", ("vc",)), (("_Color", ("vector", "_Color", unity_color(1, 1, 1, 1), None)))] + EXPOSE
    c = g.custom("Unity.SpaceGod/Line", "Points.ush",
                 "return SG_Expose(SG_LineColor(VertexColor, _Color), Exposure, SceneLuminance);", F4, inputs)
    finish(mat, g.mask(c, "", "rgb"), opacity=g.mask(c, "", "a"), wpo=wpo)
    return mat


def build_glow(mpc):
    mat = new_material("M_Glow", unreal.BlendMode.BLEND_ADDITIVE, True)
    for flag in ("used_with_niagara_sprites", "used_with_niagara_mesh_particles", "used_with_particle_sprites"):
        try:
            mat.set_editor_property(flag, True)
        except Exception:
            pass
    g = Graph(mat, mpc)
    inputs = [("UV", ("uv", 0, "rg")), ("VertexColor", ("vc",)), color3("_Color", (1, 0.8, 0.5, 1)),
              scalar("_Intensity", 1), scalar("_Falloff", 2.2)] + EXPOSE
    c = g.custom("Unity.SpaceGod/Glow", "Glow.ush",
                 "return SG_Expose3(SG_Glow(UV, VertexColor, _Color, _Intensity, _Falloff), Exposure, SceneLuminance);",
                 F3, inputs)
    finish(mat, (c, ""))
    return mat


def build_fx(mpc):
    mat = new_material("M_Fx", unreal.BlendMode.BLEND_ADDITIVE, True)
    g = Graph(mat, mpc)
    inputs = [("UV", ("uv", 0, "rg")), ("Shape", ("uv", 1, "r")), ("VertexColor", ("vc",))] + EXPOSE
    c = g.custom("Unity.SpaceGod/Fx", "Fx.ush",
                 "return SG_Expose3(SG_Fx(UV, Shape, VertexColor), Exposure, SceneLuminance);", F3, inputs)
    finish(mat, (c, ""))
    return mat


def build_asteroids(mpc):
    mat = new_material("M_Asteroids", unreal.BlendMode.BLEND_ADDITIVE, True)
    g = Graph(mat, mpc)
    vs_inputs = [("UV", ("uv", 0, "rg")), ("PositionCR", ("pos_cr_vs",)), texture("_Asteroids", "T_SG_Black"),
                 scalar("_DtDays", 0), vec4("_SunMap0", (0, 0, 0, 0)), vec4("_SunMap1", (0, 0, 0, 0)),
                 ("SunRel", ("vector_rel", "_SunRelative", (0, 0, 0, 0))),
                 scalar("_Brightness", 1), scalar("_PointScale", 1)]
    call = ("FSGAsteroidVertex v = SG_AsteroidVertex(UV, _Asteroids, _DtDays, _SunMap0, _SunMap1, SunRel, "
            "_Brightness, _PointScale, SG_VIEW_ARGS);\n")
    wpo = g.custom("Unity.SpaceGod/Asteroids (vertex)", "Asteroids.ush",
                   call + "return v.WorldCR - PositionCR;", F3, vs_inputs)
    corner = g.custom("Unity.SpaceGod/Asteroids (corner, brightness)", "Asteroids.ush",
                      call + "return float3(v.Corner, v.Brightness);", F3, vs_inputs)
    interp = mel.create_material_expression(mat, unreal.MaterialExpressionVertexInterpolator, -300, 0)
    if not mel.connect_material_expressions(corner, "", interp, "VS"):
        raise RuntimeError("M_Asteroids: could not connect the vertex interpolator")
    inputs = [("Interp", ("node", interp, "")), color3("_Color", (0.82, 0.78, 0.72, 1))] + EXPOSE
    c = g.custom("Unity.SpaceGod/Asteroids", "Asteroids.ush",
                 "return SG_Expose3(SG_Asteroid(Interp, _Color), Exposure, SceneLuminance);", F3, inputs)
    finish(mat, (c, ""), wpo=wpo)
    return mat


def build_ship(mpc):
    mat = new_material("M_Ship", unreal.BlendMode.BLEND_OPAQUE, False)
    g = Graph(mat, mpc)
    inputs = [("NormalWS", ("normal_ws",)), ("PositionCR", ("pos_cr",)), ("PositionOS", ("pos_os",)),
              ("VertexColor", ("vc",))] + LIGHTS + [
        texture("_Plating", "T_SG_Grey"), color3("_Color", (1, 1, 1, 1)), color3("_Tint", (1, 1, 1, 1)),
        scalar("_UseVertexColor", 1), scalar("_EngineGlow", 0.6), color3("_EngineColor", (0.55, 0.8, 1, 1)),
        scalar("_Highlight", 0), scalar("_PlatingScale", 10), scalar("_PlatingStrength", 0.6),
    ] + EXPOSE
    code = ("return SG_Expose3(SG_Ship(NormalWS, PositionCR, PositionOS, VertexColor, SG_LOCAL_TO_WORLD_3X3, SG_INV_SCALE, "
            "SunRel, SunColor, Sun2Rel, Sun2Color, _Plating, _PlatingSampler, _Color, _Tint, _UseVertexColor, "
            "_EngineGlow, _EngineColor, _Highlight, _PlatingScale, _PlatingStrength), Exposure, SceneLuminance);")
    c = g.custom("Unity.SpaceGod/Ship", "Ship.ush", code, F3, inputs)
    finish(mat, (c, ""))
    return mat


# Unity's environment lighting in Unity.SpaceGod's two scenes (MainMenu, UnitySpace: identical RenderSettings, the
# default procedural skybox, no baked lighting): read from the original player's built scenes (level0/level1
# RenderSettings.m_AmbientProbe) and its generated skybox reflection (sharedassets0 Default-Skybox-Cubemap, 128 px
# RGBM, 8 mips) by tools/unity_extract.py, US-T57. Each set is Unity's seven SH shader constants
# (unity_SHAr ... unity_SHC: L0+L1 per channel, L2 per channel, L2 xx-yy), in Unity's world axes.
UNITY_AMBIENT_SH = {
    "unity_SHAr": (0.0072520152, -0.0056823115, -0.0125606805, 0.1699536266),
    "unity_SHAg": (0.0114032608, 0.0413438305, -0.0197532624, 0.2113341503),
    "unity_SHAb": (0.0196566433, 0.1274849474, -0.0340533927, 0.2892073002),
    "unity_SHBr": (0.0056999680, -0.0098724226, 0.0309904749, -0.0104363170),
    "unity_SHBg": (0.0091119735, -0.0157832708, 0.0431399010, -0.0152235953),
    "unity_SHBb": (0.0163651910, -0.0283491500, 0.0531466585, -0.0220852569),
    "unity_SHC": (0.0250355694, 0.0344360620, 0.0404654965, 1.0),
}
# unity_SpecCube0 as URP's GlossyEnvironmentReflection reads it for the imported glTFs (roughness 0.9, smoothness 0.1:
# PerceptualRoughnessToMipmapLevel = 5.78, trilinear between mips 5 and 6), RGBM-decoded and projected onto L2.
UNITY_SPECCUBE_SH = {
    "_SpecCube0_SHAr": (0.0063588499, -0.0107562379, -0.0108583746, 0.1544257020),
    "_SpecCube0_SHAg": (0.0101747567, 0.0337584819, -0.0177938450, 0.1915096029),
    "_SpecCube0_SHAb": (0.0192097141, 0.1159604430, -0.0335932938, 0.2665424793),
    "_SpecCube0_SHBr": (0.0055854343, -0.0091257940, 0.0172249946, -0.0103934777),
    "_SpecCube0_SHBg": (0.0087074613, -0.0152277223, 0.0251886272, -0.0150673483),
    "_SpecCube0_SHBb": (0.0163190497, -0.0303222533, 0.0342876216, -0.0220301071),
    "_SpecCube0_SHC": (0.0140453382, 0.0203410273, 0.0252878624, 1.0),
}
# Light slots (UnityBridge / UnityRendering fill them from the scene's enabled Light components whose culling mask has
# the renderer's layer, URP's main light first): direction towards the light in world axes (zero = no light), colour
# = Light.color (linear) * intensity. SetVector values: no sRGB conversion.
LIT_LIGHTS = ["_MainLight", "_AdditionalLight0", "_AdditionalLight1"]


def build_lit(mpc):
    mat = new_material("M_Lit", unreal.BlendMode.BLEND_OPAQUE, False)
    g = Graph(mat, mpc)
    lights = []
    for slot in LIT_LIGHTS:
        lights += [vec4(slot + "Direction", (0, 0, 0, 0)), vec4(slot + "Color", (0, 0, 0, 0))]
    sh = [vec4(k, v) for k, v in UNITY_AMBIENT_SH.items()] + [vec4(k, v) for k, v in UNITY_SPECCUBE_SH.items()]
    inputs = [("UV", ("uv", 0, "rg")), ("PositionCR", ("pos_cr",))] + [
        color3("_BaseColor", (1, 1, 1, 1)), texture("_BaseMap", "T_SG_White"), scalar("_Metallic", 0),
        scalar("_Smoothness", 0.5), texture("_BumpMap", "T_SG_Bump"), scalar("_BumpScale", 1),
        color3("_EmissionColor", (0, 0, 0, 1)), texture("_EmissionMap", "T_SG_White"),
    ] + lights + sh + EXPOSE
    light_args = ", ".join(f"{slot}Direction.xyz, {slot}Color.rgb" for slot in LIT_LIGHTS)
    code = ("float3 tn = SG_UnpackUnityNormal(Texture2DSample(_BumpMap, _BumpMapSampler, UV), _BumpScale);\n"
            "float3 n = normalize(mul(tn, (float3x3)Parameters.TangentToWorld));\n"
            "float3 v = normalize(-PositionCR);\n"
            "float3 albedo = Texture2DSample(_BaseMap, _BaseMapSampler, UV).rgb * _BaseColor;\n"
            "float3 emission = Texture2DSample(_EmissionMap, _EmissionMapSampler, UV).rgb * _EmissionColor;\n"
            "return SG_Expose3(SG_Lit(n, v, albedo, _Metallic, _Smoothness, 1.0, emission, " + light_args + ", "
            + ", ".join(UNITY_AMBIENT_SH) + ", " + ", ".join(UNITY_SPECCUBE_SH) + "), Exposure, SceneLuminance);")
    c = g.custom("Universal Render Pipeline/Lit", "Lit.ush", code, F3, inputs)
    finish(mat, (c, ""))
    return mat


def _snake(name):
    """GasGiant -> gas_giant (a material's builder name)."""
    return "".join(("_" + ch.lower()) if ch.isupper() and i else ch.lower() for i, ch in enumerate(name))


BUILDERS = [build_planet, build_sun, build_ring, build_gas_giant, build_sky, build_stars, build_points, build_line,
            build_glow, build_fx, build_asteroids, build_ship, build_lit]


def main():
    # SPACEGOD_RENDER_ONLY=M_Lit,M_Ship rebuilds just those (the other .uassets stay byte-identical).
    only = [n.strip() for n in os.environ.get("SPACEGOD_RENDER_ONLY", "").split(",") if n.strip()]
    import_default_textures()
    # Rebuilding the collection gives its parameters new ids: with only some materials rebuilt (and the collection not
    # saved), they must reference the collection as it is on disk.
    mpc = unreal.load_asset(f"{MATERIALS}/{MPC_NAME}") if only else build_collection()
    built = [] if only else [mpc] + [default_texture(name) for name in DEFAULT_TEXTURES]
    failed = []
    for build in BUILDERS:
        if only and not any(build.__name__ == "build_" + _snake(n[2:]) for n in only):
            continue
        try:
            mat = build(mpc)
            built.append(mat)
            unreal.log(f"SpaceGodRender: built {mat.get_path_name()}")
        except Exception as error:
            failed.append(build.__name__)
            unreal.log_error(f"SpaceGodRender: {build.__name__} failed: {error}")
    # Save each asset itself: save_directory goes by the asset registry, which a commandlet may not have filled for
    # plugin content yet (it then silently saves nothing).
    for asset in built:
        if not eal.save_loaded_asset(asset, only_if_is_dirty=False):
            failed.append(f"save {asset.get_path_name()}")
    if failed:
        unreal.log_error(f"SpaceGodRender: FAILED {failed}")
    else:
        unreal.log("SpaceGodRender: all materials built")


main()
