#!/usr/bin/env python3
"""Apply the guarded RMA-050 presentation-contract hardening patch."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def replace_once(relative_path: str, old: str, new: str) -> None:
    path = ROOT / relative_path
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(
            f"Expected exactly one match in {relative_path}, found {count}: {old[:80]!r}"
        )
    path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


replace_once(
    "scripts/prepare_reachy_unity_assets.py",
    '''        entry = {
            "name": mesh_name,
            "source_path": source_relative,
            "source_sha256": sha256(source_path),
            "output_path": output_relative,
            "output_sha256": sha256(output_path),
            "triangle_count": triangle_count,
        }
''',
    '''        entry = {
            "name": mesh_name,
            "source_path": source_relative,
            "source_sha256": sha256(source_path),
            "source_scale": list(scale_values),
            "scale_baked_into_vertices": True,
            "output_path": output_relative,
            "output_sha256": sha256(output_path),
            "triangle_count": triangle_count,
        }
''',
)

replace_once(
    "scripts/tests/test_prepare_reachy_unity_assets.py",
    '''        head_geom = next(geom for geom in manifest["visual_geoms"] if geom["material"] == "light")
        self.assertEqual(
            [0.0, 0.0, 2.0],
            head_geom["local_pose_unity"]["position_metres"],
        )
        self.assertEqual(2, len(manifest["visual_geoms"]))
        self.assertEqual(2, len(manifest["materials"]))
        base_mesh = next(mesh for mesh in manifest["meshes"] if mesh["name"] == "base")
        self.assertEqual(1, base_mesh["triangle_count"])
        self.assertEqual(
            hashlib.sha256((self.source / "MODEL_MAP.json").read_bytes()).hexdigest(),
            manifest["source"]["model_map_sha256"],
        )
''',
    '''        head = next(body for body in manifest["bodies"] if body["name"] == "head")
        head_quaternion = head["local_pose_unity"]["quaternion_wxyz"]
        self.assertAlmostEqual(0.7071067811865476, head_quaternion[0])
        self.assertAlmostEqual(0.0, head_quaternion[1])
        self.assertAlmostEqual(-0.7071067811865475, head_quaternion[2])
        self.assertAlmostEqual(0.0, head_quaternion[3])
        head_geom = next(geom for geom in manifest["visual_geoms"] if geom["material"] == "light")
        self.assertEqual(
            [0.0, 0.0, 2.0],
            head_geom["local_pose_unity"]["position_metres"],
        )
        self.assertEqual(2, len(manifest["visual_geoms"]))
        self.assertEqual(2, len(manifest["materials"]))
        light_material = next(
            material for material in manifest["materials"] if material["name"] == "light"
        )
        self.assertEqual([0.8, 0.9, 1.0, 0.5], light_material["rgba"])
        base_mesh = next(mesh for mesh in manifest["meshes"] if mesh["name"] == "base")
        self.assertEqual([2.0, 3.0, 4.0], base_mesh["source_scale"])
        self.assertTrue(base_mesh["scale_baked_into_vertices"])
        self.assertEqual(1, base_mesh["triangle_count"])
        self.assertEqual(
            hashlib.sha256((self.source / "MODEL_MAP.json").read_bytes()).hexdigest(),
            manifest["source"]["model_map_sha256"],
        )
''',
)

replace_once(
    "scripts/tests/test_prepare_reachy_unity_assets.py",
    '''    def test_failure_preserves_previous_known_good_output(self) -> None:
''',
    '''    def test_nonfinite_mesh_scale_fails_visibly(self) -> None:
        """A mesh scale must remain finite before it is baked into vertices."""
        model_path = self.source / "reachy_mini.xml"
        model_path.write_text(
            self.fixture_mjcf().replace('scale="2 3 4"', 'scale="nan 3 4"'),
            encoding="utf-8",
        )
        model_map_path = self.source / "MODEL_MAP.json"
        model_map = json.loads(model_map_path.read_text(encoding="utf-8"))
        model_map["source_model"]["sha256"] = hashlib.sha256(
            model_path.read_bytes()
        ).hexdigest()
        model_map_path.write_text(
            json.dumps(model_map, indent=2, sort_keys=True) + "\\n",
            encoding="utf-8",
        )

        result = self.run_conversion()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("mesh base scale contains NaN or infinity", result.stderr)

    def test_failure_preserves_previous_known_good_output(self) -> None:
''',
)

old_validate_manifest = '''        private static void ValidateManifest(RenderManifest manifest)
        {
            if (manifest.schema_version != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported Unity render manifest schema: {manifest.schema_version}");
            }
            if (manifest.source == null ||
                !IsSha256(manifest.source.model_sha256))
            {
                throw new InvalidDataException(
                    "Unity render manifest source model SHA-256 is invalid.");
            }
            if (manifest.presentation == null ||
                manifest.presentation.source_cameras_included ||
                !string.Equals(
                    manifest.presentation.authoritative_transform_source,
                    "MuJoCo body snapshots only",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Unity render manifest presentation authority is invalid.");
            }
            if (manifest.bodies == null || manifest.bodies.Length != 18)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain exactly 18 model bodies.");
            }
            if (manifest.bodies.Count(body => !string.IsNullOrEmpty(body.name)) != 17)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain exactly 17 named bodies.");
            }
            if (manifest.meshes == null || manifest.meshes.Length == 0 ||
                manifest.materials == null || manifest.materials.Length == 0 ||
                manifest.visual_geoms == null || manifest.visual_geoms.Length == 0)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain meshes, materials, and visual geoms.");
            }
            if (manifest.source_cameras == null ||
                manifest.source_cameras.Length != 2 ||
                manifest.source_cameras.Any(camera => camera.included_in_presentation))
            {
                throw new InvalidDataException(
                    "MuJoCo source cameras must remain excluded from Unity presentation.");
            }
            HashSet<string> sourceCameraNames = new HashSet<string>(
                manifest.source_cameras.Select(camera => camera.name),
                StringComparer.Ordinal);
            if (!sourceCameraNames.SetEquals(
                    new[] { "studio_close", "eye_camera" }))
            {
                throw new InvalidDataException(
                    "Unity render manifest contains an unexpected source camera set.");
            }
        }
'''

new_validate_manifest = '''        private static void ValidateManifest(RenderManifest manifest)
        {
            if (manifest == null || manifest.schema_version != 1)
            {
                throw new InvalidDataException(
                    $"Unsupported Unity render manifest schema: {manifest?.schema_version}");
            }
            if (manifest.source == null ||
                !IsSha256(manifest.source.model_sha256))
            {
                throw new InvalidDataException(
                    "Unity render manifest source model SHA-256 is invalid.");
            }
            if (manifest.presentation == null ||
                manifest.presentation.source_cameras_included ||
                !string.Equals(
                    manifest.presentation.authoritative_transform_source,
                    "MuJoCo body snapshots only",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Unity render manifest presentation authority is invalid.");
            }
            if (manifest.bodies == null || manifest.bodies.Length != 18)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain exactly 18 model bodies.");
            }

            HashSet<string> bodyPaths = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.bodies.Length; ++index)
            {
                BodyEntry body = manifest.bodies[index];
                bool anonymousBody = index == 15;
                if (body == null || body.index != index ||
                    string.IsNullOrWhiteSpace(body.path) ||
                    string.IsNullOrWhiteSpace(body.parent_path) ||
                    body.local_pose_unity == null ||
                    !bodyPaths.Add(body.path) ||
                    (anonymousBody
                        ? !string.IsNullOrEmpty(body.name)
                        : string.IsNullOrWhiteSpace(body.name)))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest body {index} is malformed.");
                }
                if (!string.Equals(
                        body.parent_path,
                        "/world",
                        StringComparison.Ordinal) &&
                    !bodyPaths.Contains(body.parent_path))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest body {index} has an unknown parent.");
                }
                if (!body.path.StartsWith(
                        body.parent_path + "/",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest body {index} path is outside its parent.");
                }
            }

            if (manifest.meshes == null || manifest.meshes.Length == 0 ||
                manifest.materials == null || manifest.materials.Length == 0 ||
                manifest.visual_geoms == null || manifest.visual_geoms.Length == 0)
            {
                throw new InvalidDataException(
                    "Unity render manifest must contain meshes, materials, and visual geoms.");
            }

            Dictionary<string, MeshEntry> meshEntries =
                new Dictionary<string, MeshEntry>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.meshes.Length; ++index)
            {
                MeshEntry mesh = manifest.meshes[index];
                if (mesh == null || string.IsNullOrWhiteSpace(mesh.name) ||
                    string.IsNullOrWhiteSpace(mesh.source_path) ||
                    !IsSha256(mesh.source_sha256) ||
                    mesh.source_scale == null || mesh.source_scale.Length != 3 ||
                    !mesh.source_scale.All(IsFinite) ||
                    !mesh.scale_baked_into_vertices ||
                    string.IsNullOrWhiteSpace(mesh.output_path) ||
                    !IsSha256(mesh.output_sha256) || mesh.triangle_count <= 0 ||
                    !meshEntries.TryAdd(mesh.name, mesh))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest mesh {index} is malformed.");
                }
            }

            HashSet<string> materialNames =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.materials.Length; ++index)
            {
                MaterialEntry material = manifest.materials[index];
                if (material == null || string.IsNullOrWhiteSpace(material.name) ||
                    material.rgba == null || material.rgba.Length != 4 ||
                    material.rgba.Any(value =>
                        !IsFinite(value) || value < 0.0 || value > 1.0) ||
                    !materialNames.Add(material.name))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest material {index} is malformed.");
                }
            }

            HashSet<string> visualPaths =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < manifest.visual_geoms.Length; ++index)
            {
                VisualGeomEntry geom = manifest.visual_geoms[index];
                if (geom == null || geom.index != index ||
                    string.IsNullOrWhiteSpace(geom.path) ||
                    string.IsNullOrWhiteSpace(geom.body_path) ||
                    string.IsNullOrWhiteSpace(geom.mesh) ||
                    string.IsNullOrWhiteSpace(geom.mesh_output_path) ||
                    string.IsNullOrWhiteSpace(geom.material) ||
                    geom.local_pose_unity == null ||
                    !visualPaths.Add(geom.path) ||
                    !bodyPaths.Contains(geom.body_path) ||
                    !meshEntries.TryGetValue(geom.mesh, out MeshEntry mesh) ||
                    !string.Equals(
                        geom.mesh_output_path,
                        mesh.output_path,
                        StringComparison.Ordinal) ||
                    !materialNames.Contains(geom.material))
                {
                    throw new InvalidDataException(
                        $"Unity render manifest visual geometry {index} is malformed.");
                }
            }

            if (manifest.source_cameras == null ||
                manifest.source_cameras.Length != 2 ||
                manifest.source_cameras.Any(camera =>
                    camera == null || camera.included_in_presentation))
            {
                throw new InvalidDataException(
                    "MuJoCo source cameras must remain excluded from Unity presentation.");
            }
            HashSet<string> sourceCameraNames = new HashSet<string>(
                manifest.source_cameras.Select(camera => camera.name),
                StringComparer.Ordinal);
            if (!sourceCameraNames.SetEquals(
                    new[] { "studio_close", "eye_camera" }))
            {
                throw new InvalidDataException(
                    "Unity render manifest contains an unexpected source camera set.");
            }
        }
'''
replace_once(
    "Assets/ReachyMini/Editor/ReachyPresentationBuilder.cs",
    old_validate_manifest,
    new_validate_manifest,
)

replace_once(
    "Assets/ReachyMini/Editor/ReachyPresentationBuilder.cs",
    '''        private sealed class MeshEntry
        {
            public string name = string.Empty;
            public string output_path = string.Empty;
            public string output_sha256 = string.Empty;
            public int triangle_count;
        }
''',
    '''        private sealed class MeshEntry
        {
            public string name = string.Empty;
            public string source_path = string.Empty;
            public string source_sha256 = string.Empty;
            public double[] source_scale = Array.Empty<double>();
            public bool scale_baked_into_vertices;
            public string output_path = string.Empty;
            public string output_sha256 = string.Empty;
            public int triangle_count;
        }
''',
)

replace_once(
    "Assets/ReachyMini/Editor/ReachyPresentationBuilder.cs",
    '''            public string body_path = string.Empty;
            public string mesh = string.Empty;
            public string material = string.Empty;
''',
    '''            public string body_path = string.Empty;
            public string mesh = string.Empty;
            public string mesh_output_path = string.Empty;
            public string material = string.Empty;
''',
)

replace_once(
    "Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs",
    '''using System;
using System.Linq;
''',
    '''using System;
using System.Collections.Generic;
using System.Linq;
''',
)
replace_once(
    "Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs",
    '''using UnityEditor;
using UnityEngine;
''',
    '''using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
''',
)
replace_once(
    "Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs",
    '''                Assert.That(root.BodyCount, Is.EqualTo(18));
                Assert.That(root.VisualGeometryCount, Is.GreaterThan(0));
''',
    '''                Assert.That(root.BodyCount, Is.EqualTo(18));
                Assert.That(root.VisualGeometryCount, Is.EqualTo(161));
''',
)
replace_once(
    "Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs",
    '''                Assert.That(
                    bodies.Any(body => string.Equals(
                        body.BodyName,
                        "xl_330",
                        StringComparison.Ordinal)),
                    Is.True);

                ReachyAuthoritativeRenderer authoritativeRenderer =
''',
    '''                Assert.That(
                    bodies.Any(body => string.Equals(
                        body.BodyName,
                        "xl_330",
                        StringComparison.Ordinal)),
                    Is.True);

                Dictionary<string, ReachyPresentationBody> bodiesByPath =
                    bodies.ToDictionary(
                        body => body.BodyPath,
                        body => body,
                        StringComparer.Ordinal);
                foreach (ReachyPresentationBody body in bodies)
                {
                    int separator = body.BodyPath.LastIndexOf('/');
                    Assert.That(separator, Is.GreaterThan(0));
                    string parentPath = body.BodyPath.Substring(0, separator);
                    Transform expectedParent = string.Equals(
                        parentPath,
                        "/world",
                        StringComparison.Ordinal)
                        ? contents.transform
                        : bodiesByPath[parentPath].transform;
                    Assert.That(body.transform.parent, Is.SameAs(expectedParent));
                    Assert.That(body.transform.localScale, Is.EqualTo(Vector3.one));
                }

                ReachyAuthoritativeRenderer authoritativeRenderer =
''',
)
replace_once(
    "Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs",
    '''                Assert.That(
                    renderers.All(renderer => renderer.sharedMaterial != null),
                    Is.True);
                Assert.That(
                    filters.All(filter => filter.sharedMesh != null),
                    Is.True);

                Assert.That(
''',
    '''                Assert.That(
                    renderers.All(renderer => renderer.sharedMaterial != null),
                    Is.True);
                Assert.That(
                    filters.All(filter => filter.sharedMesh != null),
                    Is.True);
                Assert.That(
                    filters.Select(filter => filter.sharedMesh).Distinct().Count(),
                    Is.EqualTo(41));
                Assert.That(
                    renderers.Select(renderer => renderer.sharedMaterial)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(41));
                Assert.That(
                    renderers.Any(renderer => renderer.sharedMaterial.color.a < 0.999f),
                    Is.True);

                foreach (MeshFilter filter in filters)
                {
                    string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    Assert.That(
                        meshPath,
                        Does.StartWith(
                            "Assets/Generated/ReachyMini/UnityPresentation/Meshes/"));
                    Assert.That(filter.sharedMesh.vertexCount, Is.GreaterThan(0));
                    Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(1));
                    Assert.That(filter.sharedMesh.GetIndexCount(0), Is.GreaterThan(0));
                    Assert.That(filter.transform.localScale, Is.EqualTo(Vector3.one));
                    Bounds bounds = filter.sharedMesh.bounds;
                    Assert.That(IsFinite(bounds.center), Is.True);
                    Assert.That(IsFinite(bounds.extents), Is.True);
                }

                foreach (MeshRenderer renderer in renderers)
                {
                    string materialPath = AssetDatabase.GetAssetPath(
                        renderer.sharedMaterial);
                    Assert.That(
                        materialPath,
                        Does.StartWith(
                            "Assets/Generated/ReachyMini/UnityPresentation/Materials/"));
                    Color color = renderer.sharedMaterial.color;
                    Assert.That(IsFinite(color), Is.True);
                    Assert.That(color.r, Is.InRange(0f, 1f));
                    Assert.That(color.g, Is.InRange(0f, 1f));
                    Assert.That(color.b, Is.InRange(0f, 1f));
                    Assert.That(color.a, Is.InRange(0f, 1f));
                }

                Assert.That(
''',
)

replace_once(
    "Assets/ReachyMini/Tests/Editor/ReachyPresentationAssetTests.cs",
    '''        [Test]
        public void GeneratedPresentationSceneIsTheOnlyEnabledBuildScene()
        {
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Assert.That(scene, Is.Not.Null, $"Generated scene is missing: {ScenePath}");
            Assert.That(EditorBuildSettings.scenes, Has.Length.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(ScenePath));
        }
''',
    '''        [Test]
        public void GeneratedPresentationSceneIsTheOnlyEnabledBuildScene()
        {
            SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Assert.That(sceneAsset, Is.Not.Null, $"Generated scene is missing: {ScenePath}");
            Assert.That(EditorBuildSettings.scenes, Has.Length.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo(ScenePath));
        }

        [Test]
        public void GeneratedSceneLocksCameraLightingAndIndependence()
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.That(SceneManager.SetActiveScene(scene), Is.True);
                GameObject[] roots = scene.GetRootGameObjects();
                ReachyPresentationRoot presentation = roots
                    .SelectMany(root => root.GetComponentsInChildren<ReachyPresentationRoot>(true))
                    .Single();
                ReachyPresentationCamera cameraMetadata = roots
                    .SelectMany(root => root.GetComponentsInChildren<ReachyPresentationCamera>(true))
                    .Single();
                Camera camera = cameraMetadata.GetComponent<Camera>();
                Assert.That(camera, Is.Not.Null);
                Assert.That(cameraMetadata.transform.parent, Is.Null);
                Assert.That(cameraMetadata.transform.IsChildOf(presentation.transform), Is.False);
                Assert.That(cameraMetadata.gameObject.tag, Is.EqualTo("MainCamera"));
                Assert.That(cameraMetadata.Framing, Is.EqualTo("fixed_front_three_quarter"));
                Assert.That(cameraMetadata.AcceptsUserNavigation, Is.False);
                Assert.That(camera.fieldOfView, Is.EqualTo(35f));
                Assert.That(camera.nearClipPlane, Is.EqualTo(0.01f));
                Assert.That(camera.farClipPlane, Is.EqualTo(20f));
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(
                    Vector3.Distance(
                        camera.transform.position,
                        new Vector3(0.62f, 0.36f, -0.62f)),
                    Is.LessThan(1e-6f));
                Vector3 targetDirection =
                    (new Vector3(0f, 0.16f, 0f) - camera.transform.position).normalized;
                Assert.That(
                    Vector3.Angle(camera.transform.forward, targetDirection),
                    Is.LessThan(1e-4f));
                Assert.That(
                    Vector4.Distance(
                        camera.backgroundColor,
                        new Color(0.055f, 0.065f, 0.08f, 1f)),
                    Is.LessThan(1e-6f));
                Assert.That(
                    roots.SelectMany(root => root.GetComponentsInChildren<AudioListener>(true)),
                    Has.Exactly(1).Items);

                Light light = roots
                    .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                    .Single();
                Assert.That(light.transform.parent, Is.Null);
                Assert.That(light.transform.IsChildOf(presentation.transform), Is.False);
                Assert.That(light.type, Is.EqualTo(LightType.Directional));
                Assert.That(light.intensity, Is.EqualTo(1.15f));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(
                    Quaternion.Angle(
                        light.transform.rotation,
                        Quaternion.Euler(38f, -32f, 0f)),
                    Is.LessThan(1e-4f));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
                Assert.That(
                    Vector4.Distance(
                        RenderSettings.ambientLight,
                        new Color(0.34f, 0.36f, 0.4f, 1f)),
                    Is.LessThan(1e-6f));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                }
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Color value)
        {
            return IsFinite(value.r) && IsFinite(value.g) &&
                IsFinite(value.b) && IsFinite(value.a);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
''',
)

replace_once(
    "Assets/ReachyMini/Tests/PlayMode/BootstrapScenePlayModeTests.cs",
    '''            Assert.That(
                presentationCameras[0].Framing,
                Is.EqualTo("fixed_front_three_quarter"));
            Assert.That(presentationCameras[0].AcceptsUserNavigation, Is.False);
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
            Assert.That(
                UnityEngine.Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include),
                Has.Length.EqualTo(1));
''',
    '''            ReachyPresentationCamera presentationCamera = presentationCameras[0];
            Assert.That(
                presentationCamera.Framing,
                Is.EqualTo("fixed_front_three_quarter"));
            Assert.That(presentationCamera.AcceptsUserNavigation, Is.False);
            Assert.That(presentationCamera.transform.parent, Is.Null);
            Assert.That(presentationCamera.transform.IsChildOf(root.transform), Is.False);
            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include);
            Assert.That(cameras, Has.Length.EqualTo(1));
            Camera camera = cameras[0];
            Assert.That(camera, Is.SameAs(presentationCamera.GetComponent<Camera>()));
            Assert.That(camera.fieldOfView, Is.EqualTo(35f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(0.01f));
            Assert.That(camera.farClipPlane, Is.EqualTo(20f));
            Assert.That(
                Vector3.Distance(
                    camera.transform.position,
                    new Vector3(0.62f, 0.36f, -0.62f)),
                Is.LessThan(1e-6f));
            Vector3 targetDirection =
                (new Vector3(0f, 0.16f, 0f) - camera.transform.position).normalized;
            Assert.That(
                Vector3.Angle(camera.transform.forward, targetDirection),
                Is.LessThan(1e-4f));

            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include);
            Assert.That(lights, Has.Length.EqualTo(1));
            Light light = lights[0];
            Assert.That(light.transform.parent, Is.Null);
            Assert.That(light.transform.IsChildOf(root.transform), Is.False);
            Assert.That(light.type, Is.EqualTo(LightType.Directional));
            Assert.That(light.intensity, Is.EqualTo(1.15f));
            Assert.That(light.shadows, Is.EqualTo(LightShadows.Soft));
''',
)

print("RMA-050 patch applied successfully.")
