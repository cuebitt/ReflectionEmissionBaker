# Reflection Emission Baker

Lit avatar props reflecting off of an avatar is a fun effect, but realtime lights are expensive and are usually blocked by other players. This package allows you to "bake" an emission map that replicates this effect with no realtime lights!

<img width="1920" height="1080" alt="reb_demo" src="https://github.com/user-attachments/assets/4ed0f67c-8a9c-4945-8c88-d9bd04dc4791" />

There are no realtime lights being used in this clip!

## How it works

An editor window drops a temporary point light at the center of each light emitter (`Renderer.bounds.center`) and rasterizes the target mesh into UV space, triangle by triangle.

For each covered texel it interpolates world position with barycentric coordinates and accumulates falloff squared times intensity per light, keeping the brightest value. The result is dilated outward to close UV seam gaps, written out as a PNG to a unique asset path, and selected in the Project window.

Nothing is modified on your materials. You plug the baked PNG into your shader's emission mask yourself (tested with Poiyomi Toon).

For SkinnedMeshRenderers the current pose is frozen with `BakeMesh`, so pose the avatar before you bake.

## Requirements

- Unity Editor with the VRChat Avatars SDK, 3.10.5 or newer
- Target mesh with clean UVs on channel 0: fully unwrapped, no overlapping islands, no degenerate triangles. Bad UVs bake black or smeared.
- Target with a `SkinnedMeshRenderer` or `MeshFilter`. Keep scale at 1,1,1. Near-zero scale collapses everything to a point and bakes black.

## Add the package to Creator Companion

Releases publish a VPM listing from this repo, so install and updates flow through VCC:

1. Copy the listing URL for this repo.
2. Open the Creator Companion, go to Settings, then the Packages tab.
3. Press Add Repository and paste the URL. Confirm and close Settings.
4. Open your avatar project, press Manage Project, find Reflection Emission Baker in the list, and press the plus to install.

## Usage

1. Put your glow props where you want them and build the target mesh like you normally would.
2. Open Tools > Cuebitt > Reflection Emission Baker and drag the receiving GameObject into Target Mesh. Pick the Target Material slot. Only that slot is baked.
3. Add one GameObject per glow prop under Light Emitting Objects. Each `Renderer` under it becomes a point light at its bounds center.
4. Set Intensity (0.1 to 5, default 1.5) for brightness and Light Radius (0.1 to 10, default 0.1) for falloff in world units. Yellow rings in the Scene view show the radius. If the closest surface is outside the radius you get a warning and a black bake, so bump the radius up.
5. Turn on Preview to dim the scene lights and ambient and spawn hidden preview lights at the same positions the bake uses. Turn it off to restore.
6. Pick a Resolution (256, 512, 1024, 2048), a Dilation (0 to 16, default 4) to bleed lit texels into black neighbors around seams, and a Save Path (default `Assets/GeneratedTextures/EmissionMask.png`). Old bakes are never overwritten, a unique path is generated each time.
7. Press Bake Emission Mask and plug the PNG into your material's Emission Mask slot.

A sample glowstick prop ships in `Runtime/Glowsticks/`: mesh, BaseColor, Normal, MetallicSmoothness and Emission textures, plus a Poiyomi `Glowstick.mat` wired to use a baked mask.

## License

MIT. See LICENSE.
