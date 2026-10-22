#nullable enable
using System;

namespace Llamatron.Core
{
    // SBLASTER.BIN AH=3/4/5/6 and AH=2 service, without DSP/DMA hardware access.
    public sealed class SoundDriverState
    {
        public byte ActiveSample { get; private set; }
        public byte Priority { get; private set; } // AH=6 AL persists after DMA completion.
        public byte PendingSample { get; private set; }
        public byte PendingStop { get; private set; }
        private byte pendingRate, pendingPriority;
        public event Action<byte, byte>? Started;
        public event Action? Stopped;

        public static double RateHz(byte timeConstant) => 1_000_000d / (256 - timeConstant);

        public void Queue(byte sample, byte rateIndex, byte priority)
        {
            if (rateIndex >= OriginalSoundData.TimeConstants.Count)
                throw new NotSupportedException($"Unmapped sound rate index {rateIndex}.");
            PendingSample = sample; pendingRate = OriginalSoundData.TimeConstants[rateIndex]; pendingPriority = priority;
            if (ActiveSample != 0 && priority >= Priority) PendingStop = ActiveSample;
        }

        public void Stop(byte sample) => PendingStop = sample;
        public void StopAll() => PendingStop = 255;
        public void Complete() => ActiveSample = 0; // Driver 04fa/0553 leaves priority intact.

        // GAME86 CS:3e02 calls AH=2 before the frame counter and gameplay callback.
        public void Service()
        {
            if (PendingStop != 0 && (PendingStop == 255 || PendingStop == ActiveSample))
            {
                PendingStop = 0; ActiveSample = 0; Stopped?.Invoke();
            }
            if (ActiveSample != 0 || PendingSample == 0 || PendingSample >= 33) return;
            ActiveSample = PendingSample; PendingSample = 0; Priority = pendingPriority;
            Started?.Invoke(ActiveSample, pendingRate);
        }
    }
}
