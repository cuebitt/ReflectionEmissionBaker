using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cuebitt.ReflectionEmissionBaker.Editor
{
    public partial class ReflectionEmissionBaker : EditorWindow
    {

        #region Business Logic

        private void BakeEmissionMask()
        {
            if (_targetMeshObject == null)
            {
                Debug.LogError("No target mesh object assigned.");
                return;
            }

            // temp lights, one per emitter renderer
            var tempLights = new List<Light>();

            foreach (var lightEmitterObject in _lightEmitterObjects)
            {
                if (lightEmitterObject == null) continue;

                // falloff below is distance only so center is fine, hits all sides
                foreach (var r in lightEmitterObject.GetComponentsInChildren<Renderer>())
                {
                    var lightGo = new GameObject("_TempBakeLight")
                    {
                        transform =
                        {
                            position = r.bounds.center
                        }
                    };
                    var light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = Color.white;
                    light.intensity = _lightIntensity;
                    light.range = _lightRadius;
                    tempLights.Add(light);
                }
            }

            if (tempLights.Count == 0)
            {
                Debug.LogError("No valid light emitter objects found.");
                return;
            }

            var lights = tempLights.ToArray();

            try
            {
            // grab mesh + uvs off the target
            Mesh mesh;
            Transform transform;
            var bakedMesh = false;

            var skinnedRenderer = _targetMeshObject.GetComponent<SkinnedMeshRenderer>();
            var meshFilter = _targetMeshObject.GetComponent<MeshFilter>();

            if (skinnedRenderer != null)
            {
                mesh = new Mesh();

                // freeze current pose into a static mesh
                skinnedRenderer.BakeMesh(mesh);
                bakedMesh = true;
                transform = skinnedRenderer.transform;
            }
            else if (meshFilter != null)
            {
                mesh = meshFilter.sharedMesh;
                transform = meshFilter.transform;
            }
            else
            {
                Debug.LogError("Target GameObject has no SkinnedMeshRenderer or MeshFilter.");
                return;
            }

            if (_targetMeshObject.transform.lossyScale.magnitude < 1e-6f)
                Debug.LogWarning("target scale is ~0 so everything collapses to one point and bakes black, keep scale at 1,1,1");

            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var triangles = mesh.GetTriangles(_selectedMaterialIndex);

            // quick check so a too small radius warns instead of just baking black
            var minDist = float.MaxValue;
            foreach (var v in vertices)
            {
                var wp = transform.TransformPoint(v);
                foreach (var l in lights)
                    minDist = Mathf.Min(minDist, Vector3.Distance(wp, l.transform.position));
            }

            if (minDist > _lightRadius)
                Debug.LogWarning($"closest surface is {minDist:F2}m away but radius is {_lightRadius:F2}m, bump up radius or it bakes black");

            if (bakedMesh)
                DestroyImmediate(mesh);

            // fresh black texture to paint into
            var emissionMask = new Texture2D(_textureResolution, _textureResolution, TextureFormat.RGB24, false);
            var pixels = new Color[_textureResolution * _textureResolution];

            for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.black;

            // paint each triangle into uv space
            for (var t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                var wp0 = transform.TransformPoint(vertices[i0]);
                var wp1 = transform.TransformPoint(vertices[i1]);
                var wp2 = transform.TransformPoint(vertices[i2]);
                Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

                // paint it
                RasterizeTriangle(pixels, uv0, uv1, uv2, wp0, wp1, wp2, lights);
            }

            // spread edges out a bit so uv seams dont show gaps
            emissionMask.SetPixels(pixels);
            DilateEmissionMask(pixels, _textureResolution, _dilationIterations);
            emissionMask.SetPixels(pixels);
            emissionMask.Apply();

            // always pick a fresh path so we never clobber an old bake
            var uniquePath = AssetDatabase.GenerateUniqueAssetPath(_savePath);
            var directory = Path.GetDirectoryName(uniquePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(uniquePath, emissionMask.EncodeToPNG());
            AssetDatabase.Refresh();
            
            // show the result in project window
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(uniquePath);

            Debug.Log($"Emission mask saved to {uniquePath}");
            }
            finally
            {
                // always clean up temp lights even if something above fails
                foreach (var light in tempLights)
                {
                    if (light != null)
                        DestroyImmediate(light.gameObject);
                }
            }
        }
        
        
        // paints one triangle into the texture, brightest light wins per texel
        // bbox + inside test trick from scratchapixel rasterization overview
        private void RasterizeTriangle(Color[] pixels, Vector2 uv0, Vector2 uv1, Vector2 uv2,
            Vector3 wp0, Vector3 wp1, Vector3 wp2, Light[] lights)
        {
            var res = _textureResolution;

            // only check texels inside the triangle bbox
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.x, uv1.x, uv2.x) * res));
            var maxX = Mathf.Min(res, Mathf.CeilToInt(Mathf.Max(uv0.x, uv1.x, uv2.x) * res));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.y, uv1.y, uv2.y) * res));
            var maxY = Mathf.Min(res, Mathf.CeilToInt(Mathf.Max(uv0.y, uv1.y, uv2.y) * res));

            for (var py = minY; py < maxY; py++)
            for (var px = minX; px < maxX; px++)
            {
                var p = new Vector2((px + 0.5f) / res, (py + 0.5f) / res);
                var bary = Barycentric(p, uv0, uv1, uv2);

                // outside the triangle, skip
                if (bary.x < 0 || bary.y < 0 || bary.z < 0) continue;

                // blend world pos from the three corners
                var worldPos = bary.x * wp0 + bary.y * wp1 + bary.z * wp2;

                var accumulated = Color.black;

                foreach (var light in lights)
                {
                    var dist = Vector3.Distance(worldPos, light.transform.position);
                    var falloff = Mathf.Clamp01(1f - dist / _lightRadius);
                    falloff = falloff * falloff;
                    accumulated += Color.white * (falloff * light.intensity);
                }

                var idx = py * res + px;
                var existing = pixels[idx];

                pixels[idx] = new Color(
                    Mathf.Max(existing.r, accumulated.r),
                    Mathf.Max(existing.g, accumulated.g),
                    Mathf.Max(existing.b, accumulated.b),
                    1f
                );
            }
        }

        // barycentric weights for p in triangle a,b,c, negative means outside
        // dot product math from blackpawn pointinpoly, Ericson realtime collision detection
        private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1), d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
            var denom = d00 * d11 - d01 * d01;

            // flat uv triangle covers nothing, bail so we dont get nan smeared everywhere
            if (denom == 0f) return new Vector3(-1f, 0f, 0f);

            var v = (d11 * d20 - d01 * d21) / denom;
            var w = (d00 * d21 - d01 * d20) / denom;
            return new Vector3(1f - v - w, v, w);
        }

        // bleeds filled texels into black neighbors so seams dont gap, in place
        // same idea as morphological dilation (see: wikipedia) but averaging instead of max
        private static void DilateEmissionMask(Color[] pixels, int resolution, int iterations = 4)
        {
            var buffer = (Color[])pixels.Clone();

            for (var i = 0; i < iterations; i++)
            {
                for (var py = 0; py < resolution; py++)
                for (var px = 0; px < resolution; px++)
                {
                    var idx = py * resolution + px;

                    // already got light, skip
                    if (buffer[idx] != Color.black) continue;

                    var sum = Color.black;
                    var count = 0;

                    // check left right up down, average whatever has light
                    if (px > 0) AddNeighbor(buffer[idx - 1], ref sum, ref count);
                    if (px < resolution - 1) AddNeighbor(buffer[idx + 1], ref sum, ref count);
                    if (py > 0) AddNeighbor(buffer[idx - resolution], ref sum, ref count);
                    if (py < resolution - 1) AddNeighbor(buffer[idx + resolution], ref sum, ref count);

                    if (count > 0)
                        pixels[idx] = sum / count;
                }

                Array.Copy(pixels, buffer, pixels.Length);
            }
        }

        // helper for dilate, only counts texels that actually got light
        private static void AddNeighbor(Color n, ref Color sum, ref int count)
        {
            if (n == Color.black) return;
            sum += n;
            count++;
        }

        #endregion
    }
}