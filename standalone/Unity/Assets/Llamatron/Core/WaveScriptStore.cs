using System;

namespace Llamatron.Core
{
    // Mutable level DATA. CS:123c/1241 can write here with SI=256e in state 19.
    public sealed class WaveScriptStore
    {
        private readonly byte[][] scripts = OriginalWaves.CopyScripts();
        public WaveDefinition Decode(int index) => WaveDefinition.Decode(scripts[index]);
        public byte[] CopyScript(int index) => (byte[])scripts[index].Clone();

        internal void ClearChildInitializerWrites(ushort handlerOffset)
        {
            WriteWord((ushort)(handlerOffset + 8), 0);
            WriteWord((ushort)(handlerOffset + 10), 0);
        }

        private void WriteWord(ushort address, short value)
        {
            WriteByte(address, unchecked((byte)value));
            WriteByte((ushort)(address + 1), unchecked((byte)(value >> 8)));
        }

        private void WriteByte(ushort address, byte value)
        {
            for (int i = 0; i < scripts.Length; i++)
            {
                int offset = address - OriginalWaves.ScriptOffsets[i];
                if ((uint)offset >= scripts[i].Length) continue;
                scripts[i][offset] = value; return;
            }
            throw new InvalidOperationException($"Unmapped mutable wave-data byte DS:{address:x4}.");
        }
    }
}
