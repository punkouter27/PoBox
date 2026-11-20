# org.mujoco 3.15.0, embedded in PoBox

Source: https://github.com/google-deepmind/mujoco, tag `3.15.0`, folder `unity/` (pristine), plus the changes below.
Native libraries, all MuJoCo 3.15.0: `mujoco.dll` here (Windows, editor), `Assets/Plugins/MuJoCo/Android/arm64-v8a/libmujoco.so`
(phones) and `Assets/Plugins/WebGL/MuJoCo/*.a` (the browser, statically linked; how they are built: README beside them).
Python pins the same version (`pyproject.toml`); `tests/test_physics.py` replays every boxer's C MuJoCo recording with it.

Every change is marked `PoBox:` in the source:

1. `Runtime/Bindings/MjBindings.cs`: `MujocoLib` binds to `"__Internal"` in a WebGL player (MuJoCo is linked into the
   wasm), to `"mujoco"` everywhere else.
2. `Runtime/Components/MjGlobalSettings.cs`: the `eulerdamp` flag and `ls_iterations` option, which the trainer's
   models set (`training/models/*/*.xml`); `Instance` drops a destroyed settings object instead of outliving its scene.
3. `Runtime/Tools/MjcfGenerationContext.cs`: Unity's lifetime check (`!= null`) on the settings, not C#'s `?.`.
4. `Runtime/Components/MjScene.cs`, `Runtime/Components/Shapes/MjMeshFilter.cs`: `GetEntityId()` for
   `GetInstanceID()`, which is an error in Unity 6000.6.
5. `Tests/*/Mujoco.*.Tests.asmdef`: the plugin's own tests compile only with `MUJOCO_PACKAGE_TESTS` defined, so a
   project test run is PoBox's tests only.
