using System;
using System.Collections.Generic;
using UnityEngine;

namespace Llamatron.Unity
{
    [Serializable]
    public sealed class SpriteGraphic
    {
        public int bank, offset, width, height, x, y;
        public Mesh mesh;
    }

    [Serializable]
    public sealed class AtlasManifest
    {
        public int width, height;
        public SpriteGraphic[] sprites;
        public Color32[] palette;
    }

    public sealed class SpriteLibrary : ScriptableObject
    {
        public Texture2D atlas;
        public Material material;
        public SpriteGraphic[] graphics;
        public Color32[] palette;
        private Dictionary<int, SpriteGraphic> lookup;

        public SpriteGraphic Find(int offset, int bank = 0x118c)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<int, SpriteGraphic>(graphics.Length);
                foreach (SpriteGraphic graphic in graphics) lookup.Add((graphic.bank << 16) | graphic.offset, graphic);
            }
            return lookup.TryGetValue((bank << 16) | offset, out SpriteGraphic found) ? found : null;
        }
    }
}
