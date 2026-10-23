using System.Collections.Generic;
using Llamatron.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llamatron.Unity
{
    public sealed class PixelText : MonoBehaviour
    {
        private OriginalBitmapFont font;
        private Texture2D texture;
        private Material material;
        private Mesh mesh;
        private string value;
        public float PixelSize = 0.0046f;
        public Color Color = new Color(0.7f, 1, 0.85f);
        public OriginalBitmapFont Font => font;
        public Texture2D Atlas => texture;
        public string text
        {
            get => value;
            set { if (this.value == value) return; this.value = value; Rebuild(); }
        }
        public void Initialize(Material spriteMaterial)
        {
            font = new OriginalBitmapFont(Resources.Load<TextAsset>("Generated/OriginalFont").bytes);
            texture = new Texture2D(256, 96, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[256 * 96];
            for (int c = 32; c < 127; c++)
                for (int y = 0; y < 12; y++) for (int x = 0; x < 16; x++)
                    if (font.Pixel((char)c, x, y)) pixels[(95 - ((c / 16) * 12 + y)) * 256 + (c % 16) * 16 + x] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels); texture.Apply(false, true);
            material = new Material(spriteMaterial) { mainTexture = texture };
            material.SetFloat("_Indexed", 0); material.SetFloat("_ArenaFloor", 0);
            mesh = new Mesh { name = "Original font text" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        private void Rebuild()
        {
            if (font == null) return;
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var colors = new List<Color>(); var indices = new List<int>();
            string[] lines = (value ?? "").Replace('·', '-').Split('\n');
            for (int row = 0; row < lines.Length; row++)
            {
                float x = -font.Width(lines[row]) * PixelSize / 2, top = -row * 13 * PixelSize;
                foreach (char raw in lines[row])
                {
                    char c = raw >= 32 && raw < 127 ? raw : '?';
                    int i = vertices.Count; float right = x + 16 * PixelSize, bottom = top - 12 * PixelSize;
                    vertices.Add(new Vector3(x, bottom)); vertices.Add(new Vector3(right, bottom));
                    vertices.Add(new Vector3(right, top)); vertices.Add(new Vector3(x, top));
                    float u = (c % 16) / 16f, v = 1 - (c / 16 + 1) / 8f;
                    uv.Add(new Vector2(u, v)); uv.Add(new Vector2(u + 1 / 16f, v));
                    uv.Add(new Vector2(u + 1 / 16f, v + 1 / 8f)); uv.Add(new Vector2(u, v + 1 / 8f));
                    for (int k = 0; k < 4; k++) colors.Add(Color);
                    indices.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                    x += font.Advance(c) * PixelSize;
                }
            }
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        }
        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
        }
    }
}
