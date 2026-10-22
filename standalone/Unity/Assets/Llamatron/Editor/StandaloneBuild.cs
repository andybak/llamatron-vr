using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Llamatron.Unity;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Llamatron.Editor
{
    public static class StandaloneBuild
    {
        private const string Generated = "Assets/Llamatron/Resources/Generated";
        private const string ScenePath = "Assets/Llamatron/Scenes/Main.unity";

        [MenuItem("Llamatron/Prepare standalone project")]
        public static void Prepare()
        {
            BakeSprites();
            StandaloneAudioBuild.Bake();
            ConfigureXr();
            PlayerSettings.companyName = "Llamatron VR";
            PlayerSettings.productName = "Llamatron Standalone";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "org.llamatronvr.standalone");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "org.llamatronvr.standalone");
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("[LLUNITY] Scene preparation cancelled; existing edits retained.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.03f; camera.farClipPlane = 20;
            camera.transform.position = new Vector3(0, 1.6f, 0);
            camera.transform.LookAt(new Vector3(0, 0.75f, -1.75f));
            new GameObject("Standalone simulation").AddComponent<StandaloneGame>().view = camera;
            EnsureAssetFolder("Assets/Llamatron/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[LLUNITY] Prepared scene, extracted sprite meshes, and PC/Android OpenXR settings.");
        }

        private static void ConfigureXr()
        {
            const string path = "Assets/XR/Settings/LlamatronXR.asset";
            EnsureAssetFolder("Assets/XR/Settings");
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings))
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, path);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            }
            // This pinned OpenXR package exposes its settings factory on an internal class.
            Type packageSettings = Assembly.Load("Unity.XR.OpenXR.Editor").GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings", true);
            packageSettings.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                if (!settings.HasSettingsForBuildTarget(group)) settings.CreateDefaultSettingsForBuildTarget(group);
                if (!settings.HasManagerSettingsForBuildTarget(group)) settings.CreateDefaultManagerSettingsForBuildTarget(group);
                XRGeneralSettings general = settings.SettingsForBuildTarget(group);
                general.InitManagerOnStart = false;
                if (!XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                    throw new InvalidOperationException($"[LLUNITY] Could not assign OpenXR loader for {group}.");
                OpenXRSettings xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                foreach (var feature in xr.GetFeatures())
                {
                    string name = feature.GetType().Name;
                    if (name == "OculusTouchControllerProfile" || name == "KHRSimpleControllerProfile" ||
                        (group == BuildTargetGroup.Standalone && (name == "ValveIndexControllerProfile" || name == "MicrosoftMotionControllerProfile")))
                        feature.enabled = true;
                }
                EditorUtility.SetDirty(xr); EditorUtility.SetDirty(general); EditorUtility.SetDirty(general.Manager);
            }
            EditorUtility.SetDirty(settings);
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        [MenuItem("Llamatron/Bake extracted sprites")]
        public static void BakeSprites()
        {
            AssetDatabase.ImportAsset($"{Generated}/atlas.png", ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath($"{Generated}/atlas.png");
            importer.isReadable = true; importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Generated}/atlas.png");
            string indexPath = $"{Generated}/atlas-indices.png";
            AssetDatabase.ImportAsset(indexPath, ImportAssetOptions.ForceSynchronousImport);
            var indexImporter = (TextureImporter)AssetImporter.GetAtPath(indexPath);
            indexImporter.isReadable = true; indexImporter.mipmapEnabled = false; indexImporter.sRGBTexture = false;
            indexImporter.filterMode = FilterMode.Point; indexImporter.wrapMode = TextureWrapMode.Clamp;
            indexImporter.textureCompression = TextureImporterCompression.Uncompressed;
            indexImporter.npotScale = TextureImporterNPOTScale.None;
            indexImporter.SaveAndReimport();
            AtlasManifest manifest = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText($"{Generated}/atlas.json"));
            string path = $"{Generated}/SpriteLibrary.asset";
            SpriteLibrary library = AssetDatabase.LoadAssetAtPath<SpriteLibrary>(path);
            if (library == null) { library = ScriptableObject.CreateInstance<SpriteLibrary>(); AssetDatabase.CreateAsset(library, path); }
            foreach (UnityEngine.Object old in AssetDatabase.LoadAllAssetsAtPath(path))
                if (old != library) UnityEngine.Object.DestroyImmediate(old, true);
            Shader shader = Shader.Find("Llamatron/PixelSprite");
            if (shader == null) throw new InvalidOperationException("[LLUNITY] PixelSprite shader missing.");
            library.atlas = atlas;
            library.indexAtlas = AssetDatabase.LoadAssetAtPath<Texture2D>(indexPath);
            library.material = new Material(shader) { name = "Pixel sprites", mainTexture = atlas, enableInstancing = true };
            AssetDatabase.AddObjectToAsset(library.material, library);
            library.graphics = manifest.sprites;
            library.palette = manifest.palette;
            Color32[] pixels = atlas.GetPixels32();
            foreach (SpriteGraphic graphic in library.graphics)
            {
                graphic.mesh = Extrude(graphic, atlas.width, atlas.height, pixels);
                AssetDatabase.AddObjectToAsset(graphic.mesh, library);
            }
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            Debug.Log($"[LLUNITY] Baked {library.graphics.Length} original sprite meshes from {atlas.width}x{atlas.height} atlas.");
        }

        private static Mesh Extrude(SpriteGraphic g, int atlasWidth, int atlasHeight, Color32[] pixels)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var colors = new List<Color32>(); var triangles = new List<int>();
            const float depth = 0.006f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td, Color32 color)
            {
                int i = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d }); uv.AddRange(new[] { ta, tb, tc, td });
                colors.AddRange(new[] { color, color, color, color });
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            Vector2 bottomLeft = new Vector2((float)g.x / atlasWidth, (float)g.y / atlasHeight);
            Vector2 topRight = new Vector2((float)(g.x + g.width) / atlasWidth, (float)(g.y + g.height) / atlasHeight);
            var white = new Color32(255, 255, 255, 255); var edge = new Color32(158, 158, 158, 255);
            foreach (float z in new[] { -depth, depth })
                Quad(new Vector3(-0.5f, 0, z), new Vector3(0.5f, 0, z), new Vector3(0.5f, 1, z), new Vector3(-0.5f, 1, z),
                    bottomLeft, new Vector2(topRight.x, bottomLeft.y), topRight, new Vector2(bottomLeft.x, topRight.y), white);
            bool Opaque(int x, int y) => x >= 0 && y >= 0 && x < g.width && y < g.height && pixels[(g.y + y) * atlasWidth + g.x + x].a != 0;
            for (int y = 0; y < g.height; y++) for (int x = 0; x < g.width; x++)
            {
                if (!Opaque(x, y)) continue;
                float x0 = (float)x / g.width - 0.5f, x1 = (float)(x + 1) / g.width - 0.5f;
                float y0 = (float)y / g.height, y1 = (float)(y + 1) / g.height;
                Vector2 t = new Vector2((g.x + x + 0.5f) / atlasWidth, (g.y + y + 0.5f) / atlasHeight);
                if (!Opaque(x - 1, y)) Quad(new Vector3(x0, y0, -depth), new Vector3(x0, y1, -depth), new Vector3(x0, y1, depth), new Vector3(x0, y0, depth), t, t, t, t, edge);
                if (!Opaque(x + 1, y)) Quad(new Vector3(x1, y0, depth), new Vector3(x1, y1, depth), new Vector3(x1, y1, -depth), new Vector3(x1, y0, -depth), t, t, t, t, edge);
                if (!Opaque(x, y - 1)) Quad(new Vector3(x0, y0, depth), new Vector3(x1, y0, depth), new Vector3(x1, y0, -depth), new Vector3(x0, y0, -depth), t, t, t, t, edge);
                if (!Opaque(x, y + 1)) Quad(new Vector3(x0, y1, -depth), new Vector3(x1, y1, -depth), new Vector3(x1, y1, depth), new Vector3(x0, y1, depth), t, t, t, t, edge);
            }
            var mesh = new Mesh { name = $"Sprite_{g.bank:x4}_{g.offset:x4}" };
            if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        [MenuItem("Llamatron/Build Windows preview")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "windows/LlamatronStandalone.exe");

        [MenuItem("Llamatron/Build Web preview")]
        public static void BuildWeb()
        {
            Llamatron.Web.Editor.WebBuildSetup.Configure();
            Build(BuildTarget.WebGL, "web");
            string workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string output = Path.Combine(workspace, "artifacts/standalone/web");
            string buildId = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            foreach (string name in new[] { "index.html", "service-worker.js" })
            {
                string path = Path.Combine(output, name);
                File.WriteAllText(path, File.ReadAllText(path).Replace("__LLAMATRON_BUILD_ID__", buildId));
            }
            var files = new List<string> { "./" };
            foreach (string path in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(output, path).Replace('\\', '/');
                if (relative != "precache.json" && relative != "service-worker.js") files.Add(relative);
            }
            File.WriteAllText(Path.Combine(output, "precache.json"), JsonUtility.ToJson(new WebPrecache { files = files.ToArray() }));
            Debug.Log($"[LLWEB] Web app ready at {output}; build={buildId}.");
        }

        [Serializable] private sealed class WebPrecache { public string[] files; }
        public static void BuildAndroid() => BuildAndroidXr();
        [MenuItem("Llamatron/Build Android XR preview")]
        public static void BuildAndroidXr()
        {
            ConfigureAndroidVendor(false);
            Build(BuildTarget.Android, "android-xr/LlamatronStandalone.apk");
        }
        [MenuItem("Llamatron/Build Quest preview")]
        public static void BuildQuest()
        {
            ConfigureAndroidVendor(true);
            Build(BuildTarget.Android, "quest/LlamatronStandalone.apk");
        }
        private static void ConfigureAndroidVendor(bool quest)
        {
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var androidXr = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Android.AndroidXRSupportFeature>();
            var meta = settings.GetFeature<UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature>();
            if (androidXr == null || meta == null) throw new InvalidOperationException("[LLUNITY] Android platform features have not been imported.");
            androidXr.enabled = !quest; meta.enabled = quest;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.resizeableActivity = true;
            EditorUtility.SetDirty(settings); EditorUtility.SetDirty(androidXr); EditorUtility.SetDirty(meta);
            AssetDatabase.SaveAssets();
            Debug.Log($"[LLUNITY] Android vendor: {(quest ? "Quest" : "Android XR")}; ARM64 IL2CPP Vulkan.");
        }
        private static void Build(BuildTarget target, string filename)
        {
            // OpenXR validation reads the selected settings group, even when
            // BuildPlayer is explicitly passed a different platform target.
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(target);
            string workspace = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string output = Path.Combine(workspace, "artifacts/standalone", filename);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = output, target = target,
                // The OpenXR vendor hooks mutate the generated Android manifest.
                // A cached Gradle project can retain the previous vendor's intent
                // category when these features switch in the same Editor session.
                options = BuildOptions.Development | (target == BuildTarget.Android ? BuildOptions.CleanBuildCache : BuildOptions.None)
            });
            Debug.Log($"[LLUNITY] Build {target}: {report.summary.result}; errors={report.summary.totalErrors}; output={output}");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException($"[LLUNITY] {target} build failed.");
        }
    }
}
