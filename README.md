# Reflection Emission Baker

Lit avatar props reflecting off of an avatar is a fun effect, but realtime lights are expensive and are usually blocked by other players. This package allows you to "bake" an emission map that replicates this effect with no realtime lights!

<img width="1920" height="1080" alt="reb_demo" src="https://github.com/user-attachments/assets/4ed0f67c-8a9c-4945-8c88-d9bd04dc4791" />

There are no realtime lights being used in this clip!

## How it works

An editor window drops a temporary point light at the center of each light emitter (`Renderer.bounds.center`) and rasterizes the target mesh into UV space, triangle by triangle.

For each covered texel it interpolates world position with barycentric coordinates and accumulates falloff squared times intensity per light, keeping the brightest value. The result is dilated outward to close UV seam gaps, written out as a PNG to a unique asset path, and selected in the Project window.

(In simpler terms, the editor window places temporary lights, figures out which parts of the selected material's textures are affected by the temporary lights, and uses this information to generate an emission mask texture)

The baking process does not modify your avatar's model or materials. The generated PNG is written to a new asset path, and you can use it with a shader that supports emission masks (tested with Poiyomi Toon).

For SkinnedMeshRenderers the current pose is frozen with `BakeMesh`, so pose the avatar before you bake. This is also temporary, your avatar's SkinnedMeshRenderer is not modified.

## Requirements

- Unity Editor with the VRChat Avatars SDK, 3.10.5 or newer
- Target mesh with clean UVs on channel 0: fully unwrapped, no overlapping islands, no degenerate triangles. Bad UVs bake black or smeared.
- Target with a `SkinnedMeshRenderer` or `MeshFilter`. Keep scale at 1,1,1. Near-zero scale collapses everything to a point and bakes black.

## Usage

To download Reflection Emission Baker and add it to an Avatar project, add my VPM repository to VCC or ALCOM:

https://cuebitt.github.io/vpm/

1. Add your glowing props to your avatar's hierarchy and position them in their desired location.
2. Open Tools > Cuebitt > Reflection Emission Baker and drag the receiving GameObject into Target Mesh. Pick the Target Material slot. Only that slot is baked.
3. Add each glowing prop GameObject to the list in the Reflection Emission Baker window. Each `Renderer` under it temporarily becomes a point light at its bounds center.
4. Set Intensity (0.1 to 5, default 1.5) for brightness and Light Radius (0.1 to 10, default 0.1) for falloff in world units. Yellow wireframe gizmos appear in the scene view to help you set these settings.
5. Turn on Preview to dim the scene and spawn the temporary lights. The generated emission mask will look similarly to the light reflections in the preview.
6. Pick a Resolution (256, 512, 1024, 2048), a Dilation (0 to 16, default 4) to bleed lit texels into black neighbors around seams, and a Save Path (default `Assets/GeneratedTextures/EmissionMask.png`).
7. Press `Bake Emission Mask` and plug the PNG into your material's Emission Mask slot.

A sample glowing prop is located at `Runtime/Glowsticks/`. You can use this to test out Reflection Emission Baker, and you may also freely include this on any avatar you make.

## License

MIT. See LICENSE.
