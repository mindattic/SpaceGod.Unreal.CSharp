"""Reads Unity's own built-ins out of Unity.SpaceGod's built player, so the game reproduces them exactly (US-T57).

OPTIONAL, one-time importer: its output (BuiltinSkin.cs, and the constants in spacegod_render_materials.py) is
committed, so nothing in the build runs it or needs a Unity player. Only rerun it to re-extract.

    python -m pip install UnityPy          (once; any recent version)
    python tools/unity_extract.py <player>/Unity.SpaceGod_Data   (default: a Unity.SpaceGod player built next to this repo)

Writes:
  * CSharp/Script/ManagedUnrealSpaceGodCSharp/UnityCompat/Imgui/BuiltinSkin.cs (and, only where a Cpp/ tree exists,
    Cpp/Source/UnrealSpaceGod/UnityCompat/UnityBuiltinSkin.inl): Unity's runtime default skin (GUI.skin, the
    "GameSkin" in Resources/unity default resources): every style's states (background texture, text colour),
    border, margin, padding, overflow, alignment, wrapping, clipping, image position, offsets, fixed sizes, and the
    skin's textures pixel for pixel (RGBA, bilinear, sRGB, as Unity keeps them).
Prints:
  * the environment lighting of the two scenes (RenderSettings.m_AmbientProbe; the generated skybox reflection, its mip
    for the imported glTF materials' roughness projected onto L2) as Unity's SH shader constants, which
    Shared/Plugins/SpaceGodRender/Content/Python/spacegod_render_materials.py carries for M_Lit.
"""

import math
import os
import struct
import sys

import UnityPy

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(REPO), "Unity.SpaceGod", "Builds", "Windows", "Unity.SpaceGod_Data")
CS_OUT = os.path.join(REPO, "CSharp", "Script", "ManagedUnrealSpaceGodCSharp", "UnityCompat", "Imgui", "BuiltinSkin.cs")
CPP_OUT = os.path.join(REPO, "Cpp", "Source", "UnrealSpaceGod", "UnityCompat", "UnityBuiltinSkin.inl")

# GUISkin's serialized styles, in order (UnityEngine.GUISkin).
STYLE_ORDER = ["box", "button", "toggle", "label", "textField", "textArea", "window", "horizontalSlider",
               "horizontalSliderThumb", "verticalSlider", "verticalSliderThumb", "horizontalScrollbar",
               "horizontalScrollbarThumb", "horizontalScrollbarLeftButton", "horizontalScrollbarRightButton",
               "verticalScrollbar", "verticalScrollbarThumb", "verticalScrollbarUpButton", "verticalScrollbarDownButton",
               "scrollView"]
STATES = ["normal", "hover", "active", "focused", "onNormal", "onHover", "onActive", "onFocused"]
ANCHORS = ["UpperLeft", "UpperCenter", "UpperRight", "MiddleLeft", "MiddleCenter", "MiddleRight", "LowerLeft",
           "LowerCenter", "LowerRight"]
CLIPPING = ["Overflow", "Clip", "Ellipsis"]
IMAGE_POSITIONS = ["ImageLeft", "ImageAbove", "ImageOnly", "TextOnly"]
FONT_STYLES = ["Normal", "Bold", "Italic", "BoldAndItalic"]


class Reader:
    def __init__(self, data):
        self.b, self.o = data, 0

    def align(self):
        self.o = (self.o + 3) & ~3

    def i32(self):
        v = struct.unpack_from("<i", self.b, self.o)[0]
        self.o += 4
        return v

    def i64(self):
        v = struct.unpack_from("<q", self.b, self.o)[0]
        self.o += 8
        return v

    def f32(self):
        v = struct.unpack_from("<f", self.b, self.o)[0]
        self.o += 4
        return v

    def u8(self):
        v = self.b[self.o]
        self.o += 1
        return v

    def string(self):
        n = self.i32()
        v = self.b[self.o:self.o + n].decode("utf8")
        self.o += n
        self.align()
        return v

    def pptr(self):
        return self.i32(), self.i64()


def read_style(r, names):
    s = {"name": r.string()}
    for k in STATES:
        _, pid = r.pptr()
        s[k] = (names.get(pid), [r.f32() for _ in range(4)])
    for k in ("border", "margin", "padding", "overflow"):
        s[k] = [r.i32() for _ in range(4)]  # left, right, top, bottom
    r.pptr()  # font: none on every style (the skin's)
    s["fontSize"], s["fontStyle"], s["alignment"] = r.i32(), r.i32(), r.i32()
    s["wordWrap"], s["richText"] = bool(r.u8()), bool(r.u8())
    r.align()
    s["clipping"], s["imagePosition"] = r.i32(), r.i32()
    s["contentOffset"] = (r.f32(), r.f32())
    s["fixedWidth"], s["fixedHeight"] = r.f32(), r.f32()
    s["stretchWidth"], s["stretchHeight"] = bool(r.u8()), bool(r.u8())
    r.align()
    return s


def load_skin():
    env = UnityPy.load(os.path.join(DATA, "Resources", "unity default resources"))
    names, textures = {}, {}
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            t = obj.read()
            names[obj.path_id] = t.m_Name
            textures[t.m_Name] = t
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        r = Reader(obj.get_raw_data())
        r.pptr()
        r.u8()
        r.align()
        r.pptr()
        if r.string() != "GameSkin":
            continue
        r.pptr()  # the skin's font: LegacyRuntime (Arial, 13 px)
        return {k: read_style(r, names) for k in STYLE_ORDER}, textures
    raise SystemExit("no GameSkin in unity default resources")


def linear_to_srgb_byte(v):
    c = v / 255.0
    c = c * 12.92 if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055
    return max(0, min(255, int(round(c * 255))))


def pixels(tex):
    """The texture as the screen shows it, top row first. The skin's textures are linear (m_ColorSpace 1): in
    Unity.SpaceGod's linear-space project IMGUI samples them raw and writes the value sRGB-encoded, blending in gamma,
    so a texel t shows as encode(t) at its alpha (measured on the original player's model viewer: e.g. 0x55 at alpha
    0x99 over (15, 18, 26) shows 100, not the raw 57). The facades draw textures as gamma values, so the encode is
    baked in here; alpha stays raw."""
    img = tex.image.convert("RGBA")
    data = bytearray(img.tobytes())
    for i in range(0, len(data), 4):
        for k in range(3):
            data[i + k] = linear_to_srgb_byte(data[i + k])
    return img.size[0], img.size[1], bytes(data).hex()


def f(v):
    s = repr(round(float(v), 6))
    return s if "." in s or "e" in s else s + ".0"


# ------------------------------------------------------------------------------------------------------------------ C#

def cs_style(var, s):
    out = [f'            var {var} = new GUIStyle {{ name = "{s["name"]}" }};']
    for k in STATES:
        tex, col = s[k]
        if tex is None and col == [0.0, 0.0, 0.0, 1.0]:
            continue
        bg = f'T["{tex}"]' if tex else "null"
        out.append(f"            {var}.{k}.background = {bg}; {var}.{k}.textColor = new Color({', '.join(f(c) + 'f' for c in col)});")
    for k in ("border", "margin", "padding", "overflow"):
        out.append(f"            {var}.{k} = new RectOffset({', '.join(str(v) for v in s[k])});")
    out.append(f"            {var}.fontSize = {s['fontSize']}; {var}.fontStyle = FontStyle.{FONT_STYLES[s['fontStyle']]}; "
               f"{var}.alignment = TextAnchor.{ANCHORS[s['alignment']]};")
    out.append(f"            {var}.wordWrap = {str(s['wordWrap']).lower()}; {var}.richText = {str(s['richText']).lower()}; "
               f"{var}.clipping = TextClipping.{CLIPPING[s['clipping']]}; {var}.imagePosition = ImagePosition.{IMAGE_POSITIONS[s['imagePosition']]};")
    out.append(f"            {var}.contentOffset = new Vector2({f(s['contentOffset'][0])}f, {f(s['contentOffset'][1])}f); "
               f"{var}.fixedWidth = {f(s['fixedWidth'])}f; {var}.fixedHeight = {f(s['fixedHeight'])}f;")
    out.append(f"            {var}.stretchWidth = {str(s['stretchWidth']).lower()}; {var}.stretchHeight = {str(s['stretchHeight']).lower()};")
    return out


CS_FIELDS = ["box", "button", "toggle", "label", "textField", "textArea", "window", "horizontalSlider",
             "horizontalSliderThumb", "horizontalScrollbar", "horizontalScrollbarThumb", "verticalScrollbar",
             "verticalScrollbarThumb", "scrollView"]
CPP_FIELDS = CS_FIELDS + ["verticalSlider", "verticalSliderThumb"]


def used_textures(skin, fields):
    return sorted({s[k][0] for n in fields for s in [skin[n]] for k in STATES if s[k][0]})


def write_cs(skin, textures):
    lines = [
        "// GENERATED by tools/unity_extract.py from Unity.SpaceGod's built player (Resources/unity default resources,",
        "// the runtime \"GameSkin\"): do not edit; rerun the script (US-T57).",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "namespace UnityEngine",
        "{",
        "    /// <summary>Unity's runtime default skin (GUI.skin), exactly: the styles' values and the skin's textures pixel",
        "    /// for pixel. The font is the skin's: Arial at 13 px (LegacyRuntime), GUIStyle.DefaultFontSize.</summary>",
        "    internal static class BuiltinSkin",
        "    {",
        "        static Texture2D Texture(string name, int width, int height, string rgbaTopDown)",
        "        {",
        "            var t = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear,",
        "                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };",
        "            for (int y = 0; y < height; y++)",
        "                for (int x = 0; x < width; x++)",
        "                {",
        "                    int o = ((height - 1 - y) * width + x) * 8; // Unity's rows run bottom-up",
        "                    byte B(int k) => Convert.ToByte(rgbaTopDown.Substring(o + k * 2, 2), 16);",
        "                    t.SetPixel(x, y, new Color32(B(0), B(1), B(2), B(3)));",
        "                }",
        "            t.Apply(false, false);",
        "            return t;",
        "        }",
        "",
        "        internal static GUISkin Make()",
        "        {",
        "            var T = new Dictionary<string, Texture2D>(StringComparer.Ordinal);",
    ]
    for name in used_textures(skin, CS_FIELDS):
        w, h, hexs = pixels(textures[name])
        lines.append(f'            T["{name}"] = Texture("{name}", {w}, {h}, "{hexs}");')
    for n in CS_FIELDS:
        lines += cs_style(n, skin[n])
    lines.append("            return new GUISkin")
    lines.append("            {")
    lines.append("                " + ", ".join(f"{n} = {n}" for n in CS_FIELDS) + ",")
    lines.append("            };")
    lines += ["        }", "    }", "}", ""]
    with open(CS_OUT, "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write("\n".join(lines))


# ----------------------------------------------------------------------------------------------------------------- C++

def cpp_style(s, field):
    v = f"s.{field}"
    out = [f"\t\t{v} = cs::New<GUIStyle>();", f'\t\t{v}->name = "{s["name"]}";']
    for k in STATES:
        tex, col = s[k]
        if tex is None and col == [0.0, 0.0, 0.0, 1.0]:
            continue
        if tex:
            out.append(f'\t\t{v}->{k}.background = T("{tex}");')
        out.append(f"\t\t{v}->{k}.textColor = Color({', '.join(f(c) + 'f' for c in col)});")
    for k in ("border", "margin", "padding", "overflow"):
        out.append(f"\t\t{v}->{k} = RectOffset({', '.join(str(x) for x in s[k])});")
    out.append(f"\t\t{v}->fontSize = {s['fontSize']};")
    out.append(f"\t\t{v}->fontStyle = FontStyle::{FONT_STYLES[s['fontStyle']]};")
    out.append(f"\t\t{v}->alignment = TextAnchor::{ANCHORS[s['alignment']]};")
    out.append(f"\t\t{v}->wordWrap = {str(s['wordWrap']).lower()};")
    out.append(f"\t\t{v}->richText = {str(s['richText']).lower()};")
    out.append(f"\t\t{v}->clipping = TextClipping::{CLIPPING[s['clipping']]};")
    out.append(f"\t\t{v}->imagePosition = ImagePosition::{IMAGE_POSITIONS[s['imagePosition']]};")
    out.append(f"\t\t{v}->contentOffset = Vector2({f(s['contentOffset'][0])}f, {f(s['contentOffset'][1])}f);")
    out.append(f"\t\t{v}->fixedWidth = {f(s['fixedWidth'])}f;")
    out.append(f"\t\t{v}->fixedHeight = {f(s['fixedHeight'])}f;")
    out.append(f"\t\t{v}->stretchWidth = {str(s['stretchWidth']).lower()};")
    out.append(f"\t\t{v}->stretchHeight = {str(s['stretchHeight']).lower()};")
    return out


def write_cpp(skin, textures):
    lines = [
        "// GENERATED by tools/unity_extract.py from Unity.SpaceGod's built player (Resources/unity default resources,",
        "// the runtime \"GameSkin\"): do not edit; rerun the script (US-T57). Included by UnityGui.cpp inside its",
        "// namespace: Unity's runtime default skin (GUI.skin), exactly, the styles' values and the skin's textures pixel",
        "// for pixel. The font is the skin's: Arial at 13 px (LegacyRuntime).",
        "#pragma once",
        "",
        "inline cs::Ref<Texture2D> BuiltinSkinTexture(const char* name, int width, int height, const char* rgbaTopDown)",
        "{",
        "\tauto t = cs::New<Texture2D>(width, height, TextureFormat::RGBA32, false);",
        "\tt->name = name;",
        "\tt->hideFlags = HideFlags::DontSave;",
        "\tauto hex = [](char c) { return c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10; };",
        "\tfor (int y = 0; y < height; y++)",
        "\t\tfor (int x = 0; x < width; x++)",
        "\t\t{",
        "\t\t\tconst char* p = rgbaTopDown + ((height - 1 - y) * width + x) * 8; // Unity's rows run bottom-up",
        "\t\t\tfloat c[4];",
        "\t\t\tfor (int k = 0; k < 4; k++) c[k] = static_cast<float>(hex(p[k * 2]) * 16 + hex(p[k * 2 + 1])) / 255.0f;",
        "\t\t\tt->SetPixel(x, y, Color(c[0], c[1], c[2], c[3]));",
        "\t\t}",
        "\tt->Apply();",
        "\treturn t;",
        "}",
        "",
        "inline GUISkin BuiltinSkin()",
        "{",
        "\tstd::unordered_map<std::string, cs::Ref<Texture2D>> textures;",
        "\tauto T = [&textures](const char* name) { return textures.at(name); };",
    ]
    for name in used_textures(skin, CPP_FIELDS):
        w, h, hexs = pixels(textures[name])
        lines.append(f'\ttextures["{name}"] = BuiltinSkinTexture("{name}", {w}, {h}, "{hexs}");')
    lines.append("\tGUISkin s;")
    lines.append("\t{")
    for n in CPP_FIELDS:
        lines += cpp_style(skin[n], n)
    lines += ["\t}", "\treturn s;", "}", ""]
    with open(CPP_OUT, "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write("\n".join(lines).replace("\t\t", "\t\t"))


# ------------------------------------------------------------------------------------------------------------ lighting

def srgb_to_linear(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def pack_sh(sh):
    """Unity's SphericalHarmonicsL2 (normalized, [channel][9]) as unity_SHAr ... unity_SHC."""
    a = [(sh[c][3], sh[c][1], sh[c][2], sh[c][0] - sh[c][6]) for c in range(3)]
    b = [(sh[c][4], sh[c][5], sh[c][6] * 3, sh[c][7]) for c in range(3)]
    return a + b + [(sh[0][8], sh[1][8], sh[2][8], 1.0)]


def lighting(roughness=0.9):
    env = UnityPy.load(os.path.join(DATA, "level0"))
    probe = None
    for obj in env.objects:
        if obj.type.name == "RenderSettings":
            t = obj.read_typetree()
            probe = [t["m_AmbientProbe"][f"sh[{i:2d}]"] for i in range(27)]
    ambient = pack_sh([probe[0:9], probe[9:18], probe[18:27]])

    env = UnityPy.load(os.path.join(DATA, "sharedassets0.assets"), os.path.join(DATA, "sharedassets0.assets.resS"))
    cube = [o.read() for o in env.objects if o.type.name == "Cubemap"][0]
    sd = cube.m_StreamData
    with open(os.path.join(DATA, "sharedassets0.assets.resS"), "rb") as fh:
        fh.seek(sd.offset)
        raw = fh.read(sd.size)
    n0, mips = cube.m_Width, cube.m_MipCount
    face_size = sum((n0 >> m) ** 2 * 4 for m in range(mips))

    def direction(face, u, v):  # Unity / D3D cube face convention
        x, y, z = [(1, -v, -u), (-1, -v, u), (u, 1, v), (u, -1, -v), (u, -v, 1), (-u, -v, -1)][face]
        length = math.sqrt(x * x + y * y + z * z)
        return x / length, y / length, z / length, 4.0 / length ** 3

    def project(mip):
        sh = [[0.0] * 9 for _ in range(3)]
        total = 0.0
        n = n0 >> mip
        for face in range(6):
            base = face * face_size + sum((n0 >> k) ** 2 * 4 for k in range(mip))
            for j in range(n):
                for i in range(n):
                    x, y, z, w = direction(face, (i + 0.5) / n * 2 - 1, (j + 0.5) / n * 2 - 1)
                    w /= n * n
                    basis = [0.282095, 0.488603 * y, 0.488603 * z, 0.488603 * x, 1.092548 * x * y, 1.092548 * y * z,
                             0.315392 * (3 * z * z - 1), 1.092548 * x * z, 0.546274 * (x * x - y * y)]
                    k = base + (j * n + i) * 4
                    # RGBM in a linear-space project: rgb (sRGB-sampled) * 5^2.2 * a^2.2 (decodeInstructions).
                    scale = 5.0 ** 2.2 * (raw[k + 3] / 255.0) ** 2.2
                    for c in range(3):
                        col = srgb_to_linear(raw[k + c]) * scale
                        for t in range(9):
                            sh[c][t] += col * basis[t] * w
                    total += w
        norm = [0.282095, 0.488603, 0.488603, 0.488603, 1.092548, 1.092548, 0.315392, 1.092548, 0.546274]
        return [[sh[c][t] * 4 * math.pi / total * norm[t] for t in range(9)] for c in range(3)]

    pr = roughness  # perceptual roughness = 1 - smoothness = the glTF roughness
    level = pr * (1.7 - 0.7 * pr) * 6.0  # URP PerceptualRoughnessToMipmapLevel
    lo = int(math.floor(level))
    frac = level - lo
    a, b = project(lo), project(min(lo + 1, mips - 1))
    blended = [[a[c][t] * (1 - frac) + b[c][t] * frac for t in range(9)] for c in range(3)]
    spec = pack_sh(blended)
    names = ["SHAr", "SHAg", "SHAb", "SHBr", "SHBg", "SHBb", "SHC"]
    print("UNITY_AMBIENT_SH = {")
    for nm, v in zip(names, ambient):
        print(f'    "unity_{nm}": ({", ".join(f"{x:.10f}" for x in v)}),')
    print("}")
    print(f"UNITY_SPECCUBE_SH = {{  # roughness {roughness}: mip {level:.2f}")
    for nm, v in zip(names, spec):
        print(f'    "_SpecCube0_{nm}": ({", ".join(f"{x:.10f}" for x in v)}),')
    print("}")


if __name__ == "__main__":
    skin, textures = load_skin()
    write_cs(skin, textures)
    print("wrote", CS_OUT)
    # This repo is the C# track alone (US-T69): the C++ skin is written only where a Cpp/ tree exists.
    if os.path.isdir(os.path.dirname(CPP_OUT)):
        write_cpp(skin, textures)
        print("wrote", CPP_OUT)
    lighting()
