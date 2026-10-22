using System;
using System.IO;
using Llamatron.Core;
using UnityEditor;
using UnityEngine;

namespace Llamatron.Editor
{
    public static class StandaloneAudioBuild
    {
        [MenuItem("Llamatron/Bake sampled audio imports")]
        public static void Bake()
        {
            for (int id = 1; id <= 32; id++)
            {
                string path = $"Assets/Llamatron/Resources/Generated/Audio/sample-{id:00}.wav";
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) throw new InvalidOperationException($"[LLAUDIO] Missing recovered WAV: {path}");
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = false; importer.loadInBackground = false;
                importer.SaveAndReimport();
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip.channels != 1 || clip.frequency != OriginalSoundData.ContainerHz || clip.samples != OriginalSoundData.Frames[id - 1])
                    throw new InvalidOperationException($"[LLAUDIO] Import changed the sample layout: {path}");
                byte[] wav = File.ReadAllBytes(path);
                if (wav.Length < 44 + clip.samples || System.Text.Encoding.ASCII.GetString(wav, 36, 4) != "data")
                    throw new InvalidOperationException($"[LLAUDIO] Expected the recovered PCM WAV layout: {path}");
                var pcm = new float[clip.samples];
                if (!clip.GetData(pcm, 0)) throw new InvalidOperationException($"[LLAUDIO] Cannot read imported PCM: {path}");
                for (int sample = 0; sample < pcm.Length; sample++)
                    if (Math.Abs(pcm[sample] - (wav[44 + sample] - 128) / 128f) > 1f / 65536)
                        throw new InvalidOperationException($"[LLAUDIO] Import changed PCM amplitude at sample {sample}: {path}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LLAUDIO] Baked and verified all 32 PCM sample imports.");
        }
    }
}
