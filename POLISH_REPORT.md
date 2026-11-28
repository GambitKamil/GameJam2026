# Cell gameplay and animation polish

## Integration and scope

- Base gameplay was `9ae95787340400fc9b6af26584646bfd824cfd1e` on `gameplay/kamil-levels`. The original working tree contained local work, including a sandbox scene and Build Settings. This pass uses a separate worktree and branch, `gameplay/cell-motion-polish`. The checkout had no local role marker. The branch, gameplay role document and task ownership identify this pass as Kamil's `gameplay` work.
- `git fetch --all`, author/email history, Cell scene history and stash inspection found Ali-angular's `8912fb2b5668ce9ec54c15eef18f3c1777a0e011`. It was cherry-picked with `-x` as `7a0e5c495c3f7836c22ebe1541942e2b1d4673f5` before gameplay edits. `d9a8e5e` and `f8d9accf5a10048fe5bf0212fefcfded7854af03` were already included through `0923c28`. The pick was conflict-free, and the Cell scene and art tree were byte-for-byte identical to Ali's version at that point.
- Ali added a skinned gray rat, cart and material/shader dressing. The original mouse root, behavior script, collision setup and paths are still the gameplay entry points. The skinned rat has root, head and tail bones, with no leg chains. The existing readable mesh has 1,469 vertices.
- Unity 6000.3.29f1, Built-in Render Pipeline, C#, and legacy Input Manager. Cell gameplay lives under `Assets/_Game/Scripts/Gameplay/`. `SideViewPlayer` owns Rigidbody locomotion in the X/Y plane. `CellMotion` poses rigid visible mesh parts, using a hidden Quaternius Animator and manual PlayableGraph as a pose source. `BreadSupply` handles aim input and bread flight. `CellEscape` owns puzzle actions and the mouse path. `SideViewCamera` handles framing.
- Inspected live Unity MCP instances and custom tools. The original editor was connected, with animation, scene, physics, camera, build/console and editor tools. Reviewed the installed MCP package guide, validated Unity APIs by reflection and inspected project/package configuration. Animation Rigging, Cinemachine, a separate realism/motion plugin and a replacement controls package were not present. Used the Router, Unity Animation, Unity Physics, Unity C# Scripting and Unity Build Pipeline skills. The isolated editor is driven by an editor-only verifier.

## Baseline after Ali's graphics

Windows build succeeded with 0 errors and 3 existing D3D11 warnings in Ali's Weathered Surface shader (lines 72, 81 and 82). The scene scan found 684 enabled renderers, 0 missing scripts and 0 missing materials/shaders. The first editor launch also printed a Unity Search indexing exception from editor internals. The standalone Windows player launched through D3D11/PhysX. The scripted editor route completed the puzzle and restarted at `FindBread` on the second baseline run (17.71 s to `Complete`).

| Defect | Reproduction and baseline | Suspected root cause |
| --- | --- | --- |
| Bed and wake-up | Open Cell and hold Right during the intro. Root horizontal drift was 0.0000 m. The rendered figure moved up to 0.0647 m between adjacent samples. At the sit screenshot, both boots are perched on/above the bunk edge before the body comes down. | Per-frame world AABB lift moves the whole figure when any body part overlaps the bed footprint. The static standing capsule stays at the lane while the figure is physically elsewhere. |
| Floating | Stand at the bread table after walking. Idle foot gap reached 0.010 m and idle penetration was 0.000 m in the sampled frames. | Animation stride and hip samples have no support query or planted world-space foot target. |
| Limb stretch | Pick up the rod, pull the key and operate the doors. Maximum visible sleeve length change was 117.6% relative to its starting size. | `AlignHand` scales the sleeve to the target distance. Leg segments also use the distance to the requested ankle for their render length. |
| Aim | Take bread and sample a 36.9° up/right aim. The shoulder-to-hand error was 65.83°. | The aim vector drives bread flight and the preview. The figure remains in its bread-holding pose. |
| General motion | Walk to the table and back to the rod, then watch hand/handle engagement and exit framing. The capture includes rod and gate actions. | Discrete motion/clip selection and facing flips compete with root motion of the Rigidbody and frame-based target updates. Camera and IK phases have no explicit final ordering. |
| Mouse | Watch the mouse wander, intercept the bread, hit the key, land and flee. Skinned mesh has only three bones. | A sine wave translates and tilts the entire mouse every stride. The tail oscillates from elapsed time rather than following motion. |

Baseline evidence is under `D:/GameJam2026/CellMotionEvidence/Baseline/` on the verification machine. Captures include the wake start and sit, feet-down, aim, key catch, rod and exit. `BaselineRestart/verification.log` records the repeated pass with restart and the editor-only Search exception. The first trace attempted to sample a destroyed sleeve after requesting restart. The verifier now handles the scene transition separately.

## Fixes and regression

Pending the motion workstreams and a repeat of the same route.

## Suggestions (not implemented)

None.
