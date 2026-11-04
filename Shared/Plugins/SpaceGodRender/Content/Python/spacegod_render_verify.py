"""Compiles every SpaceGodRender material for the editor's shader platform and reports the result (US-T24).

A commandlet cannot render, so it never compiles material shaders: run this in the (offscreen) editor instead.
MaterialEditingLibrary.get_statistics forces each material to finish compiling, any Custom-node / .ush error is
logged ("Failed to compile Material ..."), and the editor quits when done:
    UnrealEditor-Cmd.exe CSharp/UnrealSpaceGodCSharp.uproject -ExecutePythonScript=".../spacegod_render_verify.py"
        -RenderOffscreen -unattended -nop4 -nosplash
"""

import unreal

NAMES = ["M_Planet", "M_Sun", "M_Ring", "M_GasGiant", "M_Sky", "M_Stars", "M_Points", "M_Line", "M_Glow", "M_Fx",
         "M_Asteroids", "M_Ship", "M_Lit"]

bad = []
for name in NAMES:
    mat = unreal.load_asset(f"/SpaceGodRender/Materials/{name}")
    if mat is None:
        bad.append(name)
        unreal.log_error(f"SpaceGodRender verify: {name} missing")
        continue
    stats = unreal.MaterialEditingLibrary.get_statistics(mat)
    vs, ps = stats.num_vertex_shader_instructions, stats.num_pixel_shader_instructions
    unreal.log(f"SpaceGodRender verify: {name} VS {vs} PS {ps} samplers {stats.num_samplers}")
    if ps <= 0:
        bad.append(name)
if bad:
    unreal.log_error(f"SpaceGodRender verify: FAILED {bad}")
else:
    unreal.log("SpaceGodRender verify: all materials compiled")
unreal.SystemLibrary.quit_editor()
