using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using WebXR;

namespace Llamatron.Web.Editor
{
    public static class WebBuildSetup
    {
        public static void Configure()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new InvalidOperationException("[LLWEB] Install Web Build Support for this Editor before building.");
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings))
                throw new InvalidOperationException("[LLWEB] Prepare the standalone project before configuring WebXR.");
            const BuildTargetGroup group = BuildTargetGroup.WebGL;
            if (!settings.HasSettingsForBuildTarget(group)) settings.CreateDefaultSettingsForBuildTarget(group);
            if (!settings.HasManagerSettingsForBuildTarget(group)) settings.CreateDefaultManagerSettingsForBuildTarget(group);
            XRGeneralSettings general = settings.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, typeof(WebXRLoader).FullName, group))
                throw new InvalidOperationException("[LLWEB] Could not assign the WebXR loader.");
            if (!EditorBuildSettings.TryGetConfigObject("WebXR.Settings", out WebXRSettings web))
            {
                web = ScriptableObject.CreateInstance<WebXRSettings>();
                AssetDatabase.CreateAsset(web, "Assets/XR/Settings/LlamatronWebXR.asset");
                EditorBuildSettings.AddConfigObject("WebXR.Settings", web, true);
            }
            web.VRRequiredReferenceSpace = WebXRSettings.ReferenceSpaceTypes.local_floor;
            web.VROptionalFeatures = 0;
            web.AROptionalFeatures = 0;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.WebGL.template = "PROJECT:Llamatron";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.threadsSupport = false;
            PlayerSettings.WebGL.initialMemorySize = 128;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            EditorUtility.SetDirty(web); EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager); EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[LLWEB] Configured WebGL 2, exact PCM audio and WebXR Built-in multi-camera rendering.");
        }
    }
}
