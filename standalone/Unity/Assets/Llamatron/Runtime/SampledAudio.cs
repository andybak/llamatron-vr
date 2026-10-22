using System;
using Llamatron.Core;
using UnityEngine;

namespace Llamatron.Unity
{
    public sealed class SampledAudio : MonoBehaviour
    {
        public SoundDriverState Driver { get; } = new SoundDriverState();
        public int PlayedCount { get; private set; }
        public int StoppedCount { get; private set; }
        public float PeakOutput { get; private set; }
        private readonly float[] meter = new float[256];
        private readonly AudioClip[] clips = new AudioClip[33];
        private AudioSource voice;
        private bool systemPaused;

        public void Initialize()
        {
            for (int id = 1; id <= 32; id++)
            {
                AudioClip clip = Resources.Load<AudioClip>($"Generated/Audio/sample-{id:00}");
                if (clip == null || clip.channels != 1 || clip.frequency != OriginalSoundData.ContainerHz ||
                    clip.samples != OriginalSoundData.Frames[id - 1])
                    throw new InvalidOperationException($"[LLAUDIO] Missing or changed sample {id}; recover and bake sampled audio.");
                clips[id] = clip;
            }
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false; voice.loop = false; voice.spatialBlend = 0; voice.volume = 1;
            Driver.Started += Play; Driver.Stopped += Stop;
            Debug.Log("[LLAUDIO] Ready; 32 recovered mono samples; one priority-controlled voice; DOS runtime dependencies=0.");
        }

        private void Play(byte id, byte timeConstant)
        {
            voice.Stop(); voice.clip = clips[id];
            voice.pitch = (float)(SoundDriverState.RateHz(timeConstant) / OriginalSoundData.ContainerHz);
            voice.Play(); PlayedCount++;
            if (systemPaused) voice.Pause();
        }

        private void Stop() { voice.Stop(); StoppedCount++; }

        public void PollCompletion()
        {
            if (!systemPaused && Driver.ActiveSample != 0 && !voice.isPlaying) Driver.Complete();
        }

        public void ServiceInterrupt() { PollCompletion(); Driver.Service(); }

        // Smoke diagnostics only: verifies that Unity's mixer produced a signal.
        public void ObserveOutput()
        {
            AudioListener.GetOutputData(meter, 0);
            for (int i = 0; i < meter.Length; i++) PeakOutput = Math.Max(PeakOutput, Math.Abs(meter[i]));
        }

        public void SetSystemPaused(bool value)
        {
            systemPaused = value;
            if (voice == null) return;
            if (value) voice.Pause(); else voice.UnPause();
        }

        private void OnDestroy()
        {
            Driver.Started -= Play; Driver.Stopped -= Stop;
            if (voice != null) voice.Stop();
        }
    }
}
