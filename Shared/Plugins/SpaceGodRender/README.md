# SpaceGodRender

Unity.SpaceGod's twelve hand-written shaders (`Assets/UnitySpace/Resources/Shaders`) as an Unreal 5.8 plugin that both
tracks load (`"AdditionalPluginDirectories": ["../Shared/Plugins"]` in `Cpp/` and `CSharp/`), so they draw the same
pixels. US-T24.

| Path | What |
|---|---|
| `Shaders/*.ush` | the shader maths, one file per Unity shader, ported line for line; `SpaceGodCommon.ush` = `SolarCommon.hlsl` + `PipelineCore.hlsl` + the frame conversions. Virtual path `/Plugin/SpaceGodRender/` (mapped in `SpaceGodRenderModule.cpp`, loading phase PostConfigInit) |
| `Content/Materials/M_*.uasset` | one unlit material per shader: parameter nodes and built-ins wired into Custom nodes that `#include` the `.ush` |
| `Content/Materials/MPC_SpaceGod` | Unity's shader globals |
| `Content/Textures/T_SG_*` | Unity's default textures (`white`, `black`, `grey`, `bump`) with exact values |
| `Content/Python/spacegod_render_materials.py` | builds all of the above (rerun after changing it, commit the `.uasset`s) |
| `Content/Python/spacegod_render_verify.py` | compiles every material in an offscreen editor and logs any shader error |

```
UnrealEditor-Cmd CSharp/UnrealSpaceGodCSharp.uproject -run=pythonscript -script=<abs>/Shared/Plugins/SpaceGodRender/Content/Python/spacegod_render_materials.py -unattended
UnrealEditor-Cmd CSharp/UnrealSpaceGodCSharp.uproject -ExecutePythonScript=<abs>/.../spacegod_render_verify.py -RenderOffscreen -unattended
```

Use a material through a `UMaterialInstanceDynamic` and set parameters **by the Unity name** (`SetScalarParameterValue("_Haze", …)`,
`SetVectorParameterValue("_AtmoColor", …)`, `SetTextureParameterValue("_MainTex", …)`). Globals go to the collection
(`UKismetMaterialLibrary::SetVectorParameterValue(World, MPC_SpaceGod, "_SunColor", …)`).

## Unity → Unreal rules (apply them in the engine-side code)

- **Colours.** Unity is a linear-space project: `SetColor` (and a `Color` property's default) converts sRGB → linear,
  `SetVector`/`SetGlobalVector` do not. Pass a value Unity sets with `SetColor` through the sRGB curve
  (`c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)` per rgb, alpha untouched; same for HDR values > 1); pass
  `SetVector` values (`_SunColor`, `_Flow`, `_Grs`, axes, …) as they are. The materials' defaults are already converted.
- **Vertex colours** are raw, like Unity's mesh colours: `UProceduralMeshComponent::CreateMeshSection_LinearColor(…, bSRGBConversion = false)`.
- **Axes.** Unity (x, y, z) = Unreal (x, −z, y) (ecliptic → Unity (x, z, y), ecliptic → Unreal (x, −y, z), as
  `SolSystem.cpp` does). Build meshes in Unreal's frame with that mapping (e.g. the UV sphere: +X meridian 0, +Z north,
  east −Y; the ring annulus in local XY). The shaders convert to Unity's frame where the maths names an axis (pole,
  noise lattice, triplanar plating).
- **UVs.** Write every Unity uv as `(u, 1 − v)` (Unreal's V runs from the top of an image). The shaders flip back.
- **Positions and lengths** are Unreal world units. Positions the game sets (`_SunPositionWS`, `_Sun2PositionWS`,
  `_PlanetPosRadius.xyz`, `_SunRelative`) are **world** positions: the material subtracts the camera itself, so it is
  right with or without a floating origin (Unity passed camera-relative values with the camera at 0 — identical there).
- **Exposure.** Every material ends in `SG_Expose` (`PipelineExpose`): with `_UnitySpaceSceneLuminance` 0 the colour is
  divided by the current exposure (EyeAdaptation), so it reaches the tonemapper exactly as Unity's URP drew it; > 0 is
  HDRP's physical path. For Unity's default LDR look also set the camera's post-process `ToneCurveAmount = 0`
  (`ExpandGamut = 0`, bloom off unless emulating the URP Volume stack) and prefer FXAA/none over TAA/TSR, which smear
  the 1-pixel points and lines.
- **Depth.** Unreal is reversed-Z with no far plane. Sky and Stars, which Unity pushed to the far plane in clip space,
  are moved by World Position Offset to `_SkyDistance` (default 1e15 uu; stars at half of it) around the camera: keep all
  scene geometry nearer, and give those components huge bounds / `bNeverDistanceCull`.
- **Draw order** (Unity queue → `TranslucencySortPriority`): Sky opaque; Stars −1000 (Background); Line −10; Ring,
  Points, Asteroids 0; Glow 10; Fx 20.
- **Culling.** Opaque bodies and ships are one-sided like Unity's `Cull Back`; everything Unity drew `Cull Off` is
  two-sided. A mesh that shows inside-out needs its triangle order reversed.

## Shader → material

All are unlit (`M_Lit` computes URP's lighting itself). Every material also takes `Exposure` (EyeAdaptation) and `_UnitySpaceSceneLuminance` (MPC).

| Unity shader | UE material | Blend | Parameters (Unity names kept) | Notes |
|---|---|---|---|---|
| (globals) `_SunPositionWS`, `_SunColor`, `_Sun2PositionWS`, `_Sun2Color`, `_UnitySpaceSceneLuminance`, `_UnitySpaceSunBrightness` | `MPC_SpaceGod` | | same names | `RenderBackend.Lighting`; positions in world units |
| Planet | `M_Planet` | Opaque | `_MainTex` (white), `_NormalMap` (bump), `_Color`, `_UseTexture`, `_UseNormalMap`, `_NormalStrength`, `_PoleMargin`, `_NoiseStrength`, `_NoiseScale`, `_Haze`, `_HazeColor`, `_CloudSpin`, `_AtmoColor`, `_AtmoStrength`, `_Ambient`, `_Highlight` | Normal maps import sRGB **off**, compression Default/Masks (not Normalmap/BC5: the shader reads raw xyz, as Unity's `textureType: 0, sRGBTexture: 0`) |
| Sun | `M_Sun` | Opaque | `_Color`, `_Intensity` | `_Time.y` → Time node; glare × `_UnitySpaceSunBrightness` |
| Ring | `M_Ring` | Translucent, 2-sided | `_Color`, `_InnerKm`, `_OuterKm`, `_PlanetPosRadius` (xyz world position, w radius, world units) | TexCoord0.x 0 inner → 1 outer; ring normal = local +Z |
| GasGiant | `M_GasGiant` | Opaque | `_MainTex`, `_PoleMargin`, `_AtmoColor`, `_AtmoStrength`, `_Ambient`, `_Highlight`, `_Turbulence`, `_Flow`, `_Grs`, `_GrsShape`, **`_Drift_0`…`_Drift_9`** | No array parameters in UE: `_Drift[k]` → component `k % 4` of `_Drift_{k / 4}` |
| Sky | `M_Sky` | Opaque, 2-sided | `_MainTex` (black), `_Brightness`, `_AxisX`, `_AxisY`, `_AxisZ`, UE-only `_SkyDistance` | Axes in Unreal axes (defaults are Unity's converted); cube centred on the camera, unrotated |
| Stars | `M_Stars` | Additive, 2-sided | `_Brightness`, UE-only `_PointSizePx` (1), `_SkyDistance` | No point topology in UE: each star is a quad, 4 vertices at its unit-sphere position, TexCoord0 = corner (±1, ±1), raw vertex colour |
| Points | `M_Points` | Additive, 2-sided | `_Brightness`, UE-only `_PointSizePx` (1) | as Stars, at real positions, depth-tested |
| Line | `M_Line` | Translucent, 2-sided | `_Color`, UE-only `_LineWidthPx` (1) | No line topology: each segment a ribbon of 4 vertices at its two ends; each vertex carries the *other* end (local) in TexCoord1.xy + TexCoord2.x and its side (−1/+1) in TexCoord2.y; side 0 = no expansion |
| Glow | `M_Glow` | Additive, 2-sided | `_Color`, `_Intensity`, `_Falloff` | Also flagged for Niagara sprites (particle colour = VertexColor) |
| Fx | `M_Fx` | Additive, 2-sided | (none) | Unity TEXCOORD0.xy → TexCoord0 (V flipped), TEXCOORD0.z (shape / shield age) → TexCoord1.x |
| Asteroids | `M_Asteroids` | Additive, 2-sided | `_Color`, `_Brightness`, `_PointScale`, `_DtDays`, `_SunMap0`, `_SunMap1`, `_SunRelative` (world position of the Sun), **`_Asteroids` (texture)** | No StructuredBuffer / SV_VertexID in materials: `AsteroidField.PackGpu`'s float4s go into a float RGBA texture (`PF_A32B32G32R32F`, row-major, any width); the mesh has 6 vertices per asteroid with TexCoord0 = (index, corner 0..5) at full UV precision; positions are ignored. Lengths in `_SunMap0` in world units |
| Ship | `M_Ship` | Opaque | `_Color`, `_Tint`, `_UseVertexColor`, `_EngineGlow`, `_EngineColor`, `_Highlight`, `_Plating` (grey), `_PlatingScale`, `_PlatingStrength` | `ship_plating.png` imports sRGB **off**, uncompressed or Default (R detail, G/B normal) |

| URP Lit (`Universal Render Pipeline/Lit`; glTFast's `glTF-pbrMetallicRoughness`, a URP Lit target) | `M_Lit` | Opaque | `_BaseColor`, `_BaseMap` (white), `_Metallic`, `_Smoothness` (= 1 − glTF roughness), `_BumpMap` (bump), `_BumpScale`, `_EmissionColor`, `_EmissionMap` (white); lights `_MainLightDirection`/`Color`, `_AdditionalLight0…1Direction`/`Color`; environment `unity_SHAr…unity_SHC`, `_SpecCube0_SHAr…SHC` | US-T57. The one lit material and the only one Unity's `Light` components reach (the game's shaders are unlit). `Shaders/Lit.ush` is URP 17's forward PBR line for line: `InitializeBRDFData`, `DirectBRDFSpecular`, `LightingPhysicallyBased`, `GlobalIllumination` (ambient SH × diffuse + glossy reflection × `EnvironmentBRDF`), emission. Light slots are SetVector values: direction **towards** the light in world axes (zero = none), colour = `Light.color.linear × intensity`. The SH defaults are Unity.SpaceGod's scenes' own environment (both scenes: default procedural skybox, nothing baked), read from the original player by `tools/unity_extract.py`: `RenderSettings.m_AmbientProbe` as Unity's seven shader constants, and the generated skybox reflection (`unity_SpecCube0`, RGBM) at the mip URP picks for the imported glTFs' roughness 0.9 (5.78) projected onto L2. Normal maps as Unity imports them (green up); the shader flips green for Unreal's V |

Unity's unused `_MainTex_ST` / `_NormalMap_ST` / `_Plating_ST` (declared, never applied) have no counterpart.

`spacegod_render_materials.py` rebuilds every material; `SPACEGOD_RENDER_ONLY=M_Lit` (comma-separated) rebuilds just those, so
the other `.uasset`s stay untouched.

## Not (yet) exactly Unity

- Stars/Points: a 1-pixel quad lands on one pixel or straddles two depending on sub-pixel position; Unity's point is
  always exactly one pixel. Lines are 1-pixel ribbons, not hardware lines (no gaps/joins handling at strip corners).
- The URP Volume stack (UrpRenderBackend's adapting exposure meter, bloom, colour grading) is engine-side, not in here.
- `M_Lit`'s glossy reflection is the skybox cube's L2 projection at one mip (roughness 0.9, all the imported glTFs):
  exact in the low frequencies URP samples there; a smoother URP Lit material would need that mip's projection.
  Directional lights only (all Unity.SpaceGod creates), no shadows (its lights cast none).
- Untested on screen in this ticket: the materials compile for SM5/SM6 (`spacegod_render_verify.py`), but no view
  draws them yet.
