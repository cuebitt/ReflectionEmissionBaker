using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Cuebitt.ReflectionEmissionBaker.Editor
{
    public partial class ReflectionEmissionBaker : EditorWindow
    {
        // what to bake onto
        private GameObject _targetMeshObject;
        private string[] _materialNames = Array.Empty<string>();
        private int _selectedMaterialIndex;

        // light emitters picked in the ui
        private readonly List<GameObject> _lightEmitterObjects = new();

        // bake settings
        private float _lightIntensity = 1.5f;
        private float _lightRadius = 0.1f;

        // preview stuff, cleaned up when you toggle off or close
        private bool _previewEnabled;
        private readonly List<Light> _previewLights = new();
        private readonly List<Light> _dimmedLights = new();
        private readonly List<float> _dimmedIntensities = new();
        private float _savedAmbientIntensity;
        private bool _sceneDimmed;

        // output
        private int _textureResolution = 512;
        private int _dilationIterations = 4;
        private string _savePath = "Assets/GeneratedTextures/EmissionMask.png";

        private GUIStyle _paddingStyle;

        private bool _hasResized;
        private Vector2 _scrollPos;


        private void OnEnable()
        {
            _paddingStyle = new GUIStyle
            {
                padding = new RectOffset(10, 10, 10, 10)
            };

            SceneView.duringSceneGui += DrawLightRadiusGizmos;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawLightRadiusGizmos;
            ClearPreviewLights();
            RestoreSceneLighting();
            _previewEnabled = false;
        }

        // draws the falloff rings in scene view, same centers the bake uses
        private void DrawLightRadiusGizmos(SceneView view)
        {
            if (_lightEmitterObjects == null || _lightRadius <= 0f) return;
            Handles.color = new Color(1f, 0.9f, 0.3f);
            foreach (var emitter in _lightEmitterObjects)
            {
                if (emitter == null) continue;
                foreach (var r in emitter.GetComponentsInChildren<Renderer>())
                {
                    var c = r.bounds.center;
                    Handles.DrawWireDisc(c, Vector3.up, _lightRadius);
                    Handles.DrawWireDisc(c, Vector3.right, _lightRadius);
                    Handles.DrawWireDisc(c, Vector3.forward, _lightRadius);
                }
            }
        }

        private void SetPreview(bool enabled)
        {
            _previewEnabled = enabled;

            if (enabled)
            {
                DimSceneForPreview();
                SpawnPreviewLights();
            }
            else
            {
                ClearPreviewLights();
                RestoreSceneLighting();
            }
        }

        // kills scene lights + ambient so preview reads clearly, restores on toggle off
        private void DimSceneForPreview()
        {
            if (_sceneDimmed) return;

            _savedAmbientIntensity = RenderSettings.ambientIntensity;
            RenderSettings.ambientIntensity = 0f;

            _dimmedLights.Clear();
            _dimmedIntensities.Clear();

            foreach (var l in FindObjectsOfType<Light>())
            {
                if (l == null) continue;
                _dimmedLights.Add(l);
                _dimmedIntensities.Add(l.intensity);
                l.intensity = 0f;
            }

            _sceneDimmed = true;
        }

        private void RestoreSceneLighting()
        {
            if (!_sceneDimmed) return;

            RenderSettings.ambientIntensity = _savedAmbientIntensity;

            for (var i = 0; i < _dimmedLights.Count; i++)
            {
                if (_dimmedLights[i] != null)
                    _dimmedLights[i].intensity = _dimmedIntensities[i];
            }

            _dimmedLights.Clear();
            _dimmedIntensities.Clear();
            _sceneDimmed = false;
        }

        // same spot as the bake uses, hidden so it never touches the scene file
        private void SpawnPreviewLights()
        {
            ClearPreviewLights();

            foreach (var emitter in _lightEmitterObjects)
            {
                if (emitter == null) continue;

                foreach (var r in emitter.GetComponentsInChildren<Renderer>())
                {
                    var go = new GameObject("_PreviewBakeLight") { hideFlags = HideFlags.HideAndDontSave };
                    go.transform.position = r.bounds.center;

                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = Color.white;
                    light.intensity = _lightIntensity;
                    light.range = _lightRadius;

                    _previewLights.Add(light);
                }
            }
        }

        // just pushes slider values into existing lights, respawns only when emitters changed
        private void UpdatePreviewLights()
        {
            var count = 0;
            foreach (var emitter in _lightEmitterObjects)
            {
                if (emitter == null) continue;
                count += emitter.GetComponentsInChildren<Renderer>().Length;
            }

            if (count != _previewLights.Count)
            {
                SpawnPreviewLights();
                return;
            }

            foreach (var l in _previewLights)
            {
                if (l == null) continue;
                l.intensity = _lightIntensity;
                l.range = _lightRadius;
            }
        }

        private void ClearPreviewLights()
        {
            foreach (var l in _previewLights)
            {
                if (l != null)
                    DestroyImmediate(l.gameObject);
            }
            _previewLights.Clear();
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

            EditorGUILayout.HelpBox("Target mesh needs clean UVs (channel 0): fully unwrapped, no overlapping islands, no degenerate triangles. Bad UVs bake black or smeared masks.", MessageType.Warning);
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

            // deferred so asset dialogs dont break layout
            if (GUILayout.Button("Bake Emission Mask", GUILayout.Height(40)))
                EditorApplication.delayCall += BakeEmissionMask;

            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndScrollView();

            if (GUI.changed)
            {
                // keep radius rings in sync while dragging sliders
                SceneView.RepaintAll();

                if (_previewEnabled)
                    UpdatePreviewLights();
            }

            // fit window height once on open, then let user resize freely
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

        [MenuItem("Tools/Cuebitt/Reflection Emission Baker")]
        private static void Open()
        {
            GetWindow<ReflectionEmissionBaker>("[TT] Reflection Emission Baker");
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
                    0.1f, 10f);
            var preview = EditorGUILayout.Toggle(
                new GUIContent("Preview", "Darkens other lights and shows live bake lights in the scene."),
                _previewEnabled);
            if (preview != _previewEnabled)
                SetPreview(preview);
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

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        #endregion

    }
}