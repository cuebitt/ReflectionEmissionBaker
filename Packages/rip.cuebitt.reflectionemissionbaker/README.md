# Reflection Emission Baker

If you stick glowsticks in a jacket pocket, the jacket should pick up some of that light. This bakes that kind of fake bounce light into an emission mask texture, so you get the glow with no realtime lights. By Cuebitt.

## Usage

1. Open Tools > Cuebitt > Reflection Emission Baker and drag the receiving GameObject into Target Mesh. Pick the Target Material slot. Only that slot is baked.
2. Add one GameObject per glow prop under Light Emitting Objects. Each `Renderer` under it becomes a point light at its bounds center.
3. Set Intensity and Light Radius. Yellow rings in the Scene view show the radius. Turn on Preview for a live check, it dims the rest of the scene.
4. Pick a Resolution, Dilation, and Save Path, then press Bake Emission Mask. Plug the PNG into your shader's emission mask.

The target needs clean, non-overlapping UVs on channel 0 and scale at 1,1,1, or it bakes black or smeared. Skinned meshes bake from the current pose. Source materials are never modified. A sample Poiyomi `Glowstick.mat` ships in `Runtime/Glowsticks/`. See the root README for full details.

## License

MIT.
