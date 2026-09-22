using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/*
 * todo:
 * - add temp light preview
 */

namespace Cuebitt.LightEmissionBaker.Editor
{
    public class LightEmissionBaker : EditorWindow
    {
        // Target section
        private GameObject _targetMeshObject;
        private string[] _materialNames = Array.Empty<string>();
        private int _selectedMaterialIndex;

        // Light emitters section
        private readonly List<GameObject> _lightEmitterObjects = new();
        private SerializedProperty _lightEmitterObjectsListProperty;

        // Light section
        private float _lightIntensity = 1.5f;
        private float _lightRadius = 0.1f; // Falloff radius in world units
        
        // Output section
        private int _textureResolution = 512;
        private int _dilationIterations = 4;
        private string _savePath = "Assets/GeneratedTextures/EmissionMask.png";
        private bool _overwriteOutputFile;
        
        // UGUI Styles
        private GUIStyle _paddingStyle;
        
        private SerializedObject _serializedWindow;
        private bool _hasResized;
        private Vector2 _scrollPos;


        private void OnEnable()
        {
            // Create GUIStyle(s)
            _paddingStyle = new GUIStyle
            {
                padding = new RectOffset(10, 10, 10, 10)
            };
        }

        private void OnGUI()
        {
            // header
            GUILayout.Label("Emission Baker", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 10, 10)
            });
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(0, 1), new Color(0.3f, 0.3f, 0.3f));
            EditorGUILayout.Space();
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.BeginVertical(_paddingStyle);

            DrawTargetSection();
            EditorGUILayout.Space();

            DrawLightEmitterSection();
            EditorGUILayout.Space();

            DrawLightSection();
            EditorGUILayout.Space();

            DrawOutputSection();
            EditorGUILayout.Space();

            // Bake Emission Mask button
            if (GUILayout.Button("Bake Emission Mask", GUILayout.Height(40)))
                BakeEmissionMask();

            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndScrollView();

            // Resize the window (vertically) once when the window opens
            if (!_hasResized && Event.current.type == EventType.Repaint)
            {
                var height = GUILayoutUtility.GetLastRect().yMax + 10;
                minSize = new Vector2(minSize.x, height);
                maxSize = new Vector2(maxSize.x, height);

                _hasResized = true;
                // Unlock max size so the user can resize freely after initial fit
                maxSize = new Vector2(600, 4000);
            }
        }

        [MenuItem("Tools/Cuebitt/Light Emission Baker")]
        private static void Open()
        {
            GetWindow<LightEmissionBaker>("[TT] Light Emission Baker");
        }

        #region GUI Sections

        private void DrawTargetSection()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Target", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");
            _targetMeshObject =
                (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("Target Mesh",
                        "The GameObject containing the SkinnedMeshRenderer or MeshRenderer to bake onto."),
                    _targetMeshObject, typeof(GameObject), true);
            if (_targetMeshObject != null)
            {
                var renderer = _targetMeshObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    _materialNames = Array.ConvertAll(renderer.sharedMaterials, m => m != null ? m.name : "(Missing)");
                    _selectedMaterialIndex =
                        EditorGUILayout.Popup(
                            new GUIContent("Target Material",
                                "Material to bake the emission onto (does not modify the material)."),
                            _selectedMaterialIndex, _materialNames);
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawLightEmitterSection()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Light Emitting Objects", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            for (var i = 0; i < _lightEmitterObjects.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _lightEmitterObjects[i] =
                    (GameObject)EditorGUILayout.ObjectField(_lightEmitterObjects[i], typeof(GameObject), true);
                if (GUILayout.Button("-", GUILayout.Width(20)))
                    _lightEmitterObjects.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Light Emitter"))
                _lightEmitterObjects.Add(null);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawLightSection()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Light Configuration", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _lightIntensity =
                EditorGUILayout.Slider(
                    new GUIContent("Intensity", "Controls how bright the baked light appears on the emission mask."),
                    _lightIntensity, 0.1f, 5f);
            _lightRadius =
                EditorGUILayout.Slider(
                    new GUIContent("Light Radius",
                        "The falloff radius in world units. Increase if the light doesn't reach the model."),
                    _lightRadius,
                    0.1f, 3f);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Label("Output", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical("box");
            _textureResolution = EditorGUILayout.IntPopup(
                new GUIContent("Resolution", "Output texture resolution in pixels."), _textureResolution,
                new[] { new GUIContent("256"), new GUIContent("512"), new GUIContent("1024"), new GUIContent("2048") },
                new[] { 256, 512, 1024, 2048 });
            _dilationIterations =
                EditorGUILayout.IntSlider(
                    new GUIContent("Dilation", "Expands filled texels outward to fill seam gaps between UV islands."),
                    _dilationIterations, 0, 16);
            _savePath = EditorGUILayout.TextField(new GUIContent("Save Path", "Path of the generated texture."),
                _savePath);
            _overwriteOutputFile = EditorGUILayout.Toggle(
                new GUIContent("Overwrite File", "When enabled, file will be overwritten if it already exists.."),
                _overwriteOutputFile);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Business Logic

        private void BakeEmissionMask()
        {
            // Spawn a temporary light at each emissive renderer's position
            var tempLights = new List<Light>();
            foreach (var lightEmitterObject in _lightEmitterObjects)
            {
                if (lightEmitterObject == null) continue; // skip invalid objects
                
                // Spawn a temporary light source on each renderer associated with each light emitter object
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

            // Get the mesh and UV data from the target model
            Mesh mesh;
            Transform transform;
            
            var skinnedRenderer = _targetMeshObject.GetComponent<SkinnedMeshRenderer>();
            var meshFilter = _targetMeshObject.GetComponent<MeshFilter>();

            if (skinnedRenderer != null)
            {
                mesh = new Mesh();
                skinnedRenderer.BakeMesh(mesh); // bakes the current pose into a static mesh
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

            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var triangles = mesh.GetTriangles(_selectedMaterialIndex);

            // Create output texture
            var emissionMask = new Texture2D(_textureResolution, _textureResolution, TextureFormat.RGB24, false);
            var pixels = new Color[_textureResolution * _textureResolution];
            // Initialize to black
            for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.black;

            // 4. For each triangle, sample UV space and accumulate light contribution
            for (var t = 0; t < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                var wp0 = transform.TransformPoint(vertices[i0]);
                var wp1 = transform.TransformPoint(vertices[i1]);
                var wp2 = transform.TransformPoint(vertices[i2]);
                Vector2 uv0 = uvs[i0], uv1 = uvs[i1], uv2 = uvs[i2];

                // Rasterize the triangle in UV space
                RasterizeTriangle(pixels, uv0, uv1, uv2, wp0, wp1, wp2, lights);
            }
            
            // Create the emission mask texture and dilate
            emissionMask.SetPixels(pixels);
            DilateEmissionMask(pixels, _textureResolution, _dilationIterations);
            emissionMask.SetPixels(pixels);
            emissionMask.Apply();

            // Remove all temporary lights
            foreach (var light in tempLights)
                DestroyImmediate(light.gameObject);
            
            // Save as PNG
            string uniquePath;
            if (!_overwriteOutputFile)
            {
                uniquePath = GetUniqueSavePath(_savePath);
            }
            else
            {
                uniquePath = _savePath;
                if (File.Exists(_savePath))
                {
                    var overwrite = EditorUtility.DisplayDialog(
                        "Overwrite File?",
                        $"A file already exists at:\n{_savePath}\n\nDo you want to overwrite it?",
                        "Overwrite",
                        "Cancel");
                    if (!overwrite)
                        return;
                }
            }
            File.WriteAllBytes(uniquePath, emissionMask.EncodeToPNG());
            AssetDatabase.Refresh();
            
            // Select the generated texture in the file explorer
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(uniquePath);

            // Clean up temporary lights
            foreach (var light in tempLights)
                DestroyImmediate(light.gameObject);
            Debug.Log($"Emission mask saved to {uniquePath}");
        }
        
        
        /// <summary>
        /// Rasterizes a single mesh triangle into UV space, accumulating light contributions
        /// from all temporary bake lights onto the corresponding texels in the output pixel array.
        /// </summary>
        /// <param name="pixels">The flat pixel array representing the emission texture, indexed as y * resolution + x.</param>
        /// <param name="uv0">UV coordinate of the first triangle vertex.</param>
        /// <param name="uv1">UV coordinate of the second triangle vertex.</param>
        /// <param name="uv2">UV coordinate of the third triangle vertex.</param>
        /// <param name="wp0">World-space position of the first triangle vertex.</param>
        /// <param name="wp1">World-space position of the second triangle vertex.</param>
        /// <param name="wp2">World-space position of the third triangle vertex.</param>
        /// <param name="lights">Array of temporary point lights representing light emission sources.</param>

        private void RasterizeTriangle(Color[] pixels, Vector2 uv0, Vector2 uv1, Vector2 uv2,
            Vector3 wp0, Vector3 wp1, Vector3 wp2, Light[] lights)
        {
            var res = _textureResolution;
            // Bounding box of the triangle in pixel space
            var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.x, uv1.x, uv2.x) * res));
            var maxX = Mathf.Min(res, Mathf.CeilToInt(Mathf.Max(uv0.x, uv1.x, uv2.x) * res));
            var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(uv0.y, uv1.y, uv2.y) * res));
            var maxY = Mathf.Min(res, Mathf.CeilToInt(Mathf.Max(uv0.y, uv1.y, uv2.y) * res));

            for (var py = minY; py < maxY; py++)
            for (var px = minX; px < maxX; px++)
            {
                var p = new Vector2((px + 0.5f) / res, (py + 0.5f) / res);
                var bary = Barycentric(p, uv0, uv1, uv2);
                if (bary.x < 0 || bary.y < 0 || bary.z < 0) continue; // outside triangle

                // Interpolate world position using barycentric coords
                var worldPos = bary.x * wp0 + bary.y * wp1 + bary.z * wp2;

                // Accumulate contribution from each light emitter's temp light
                var accumulated = Color.black;
                foreach (var light in lights)
                {
                    var dist = Vector3.Distance(worldPos, light.transform.position);
                    var falloff = Mathf.Clamp01(1f - dist / _lightRadius);
                    falloff = falloff * falloff; // quadratic falloff
                    accumulated += Color.white * (falloff * _lightIntensity * light.intensity);
                }

                accumulated.a = 1f;
                var idx = py * res + px;
                // Additive blend (brightest wins)
                var existing = pixels[idx];
                pixels[idx] = new Color(
                    Mathf.Max(existing.r, accumulated.r),
                    Mathf.Max(existing.g, accumulated.g),
                    Mathf.Max(existing.b, accumulated.b),
                    1f
                );
            }
        }
        
        /// <summary>
        /// Computes the barycentric coordinates of point <paramref name="p"/> relative to
        /// triangle (<paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>) in 2D.
        /// The returned vector's components (u, v, w) sum to 1 when the point is inside the
        /// triangle, and at least one component is negative when outside.
        /// </summary>
        /// <param name="p">The point to test, in the same 2D space as the triangle.</param>
        /// <param name="a">First vertex of the triangle.</param>
        /// <param name="b">Second vertex of the triangle.</param>
        /// <param name="c">Third vertex of the triangle.</param>
        /// <returns>
        /// A <see cref="Vector3"/> where x, y, z are the barycentric weights for vertices
        /// a, b, and c respectively. Use these to interpolate any per-vertex attribute
        /// (e.g. world position) across the triangle's surface.
        /// </returns>
        private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
            float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1);
            float d11 = Vector2.Dot(v1, v1), d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
            var denom = d00 * d11 - d01 * d01;
            var v = (d11 * d20 - d01 * d21) / denom;
            var w = (d00 * d21 - d01 * d20) / denom;
            return new Vector3(1f - v - w, v, w);
        }
        
        /// <summary>
        /// Dilates filled texels outward into empty (black) neighboring texels to eliminate
        /// seams and gaps at UV island borders. Each iteration expands filled regions by one
        /// pixel in the four cardinal directions.
        /// </summary>
        /// <param name="pixels">The flat pixel array to dilate, modified in place.</param>
        /// <param name="resolution">The width and height of the texture in pixels.</param>
        /// <param name="iterations">
        /// Number of dilation passes to perform. Higher values produce wider padding around
        /// UV islands. Defaults to 4, which is sufficient for most resolutions.
        /// </param>
        private static void DilateEmissionMask(Color[] pixels, int resolution, int iterations = 4)
        {
            var buffer = (Color[])pixels.Clone();

            for (var i = 0; i < iterations; i++)
            {
                for (var py = 0; py < resolution; py++)
                for (var px = 0; px < resolution; px++)
                {
                    var idx = py * resolution + px;
                    if (buffer[idx] != Color.black) continue; // already filled, skip

                    // Sample 4-connected neighbors
                    var sum = Color.black;
                    var count = 0;
                    if (px > 0)
                    {
                        var n = buffer[idx - 1];
                        if (n != Color.black)
                        {
                            sum += n;
                            count++;
                        }
                    }

                    if (px < resolution - 1)
                    {
                        var n = buffer[idx + 1];
                        if (n != Color.black)
                        {
                            sum += n;
                            count++;
                        }
                    }

                    if (py > 0)
                    {
                        var n = buffer[idx - resolution];
                        if (n != Color.black)
                        {
                            sum += n;
                            count++;
                        }
                    }

                    if (py < resolution - 1)
                    {
                        var n = buffer[idx + resolution];
                        if (n != Color.black)
                        {
                            sum += n;
                            count++;
                        }
                    }

                    if (count > 0)
                        pixels[idx] = sum / count;
                }

                Array.Copy(pixels, buffer, pixels.Length);
            }
        }
        
        /// <summary>
        /// Ensures the given file path is unique by appending an incrementing number before
        /// the extension if a file already exists at that path. For example, if
        /// <c>EmissionMap.png</c> exists, returns <c>EmissionMap_1.png</c>, then
        /// <c>EmissionMap_2.png</c>, and so on until an unused path is found.
        /// </summary>
        /// <param name="path">The desired save path, including directory and extension.</param>
        /// <returns>
        /// The original <paramref name="path"/> if no file exists there, otherwise the first
        /// numbered variant that does not already exist on disk.
        /// </returns>
        private static string GetUniqueSavePath(string path)
        {
            if (!File.Exists(path))
                return path;
        
            var dir       = Path.GetDirectoryName(path);
            var fileNameWithoutExtension      = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
        
            var i = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(dir!, $"{fileNameWithoutExtension}_{i}{extension}");
                i++;
            } while (File.Exists(newPath));
        
            return newPath;
        }

        #endregion
    }
}