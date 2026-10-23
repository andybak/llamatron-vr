using Llamatron.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Llamatron.Unity
{
    // A scene surface, so menus share the player's camera and stereo rendering.
    public sealed class PixelPanel : MonoBehaviour
    {
        private const int Width = 400, Height = 280;
        private readonly Color32[] pixels = new Color32[Width * Height];
        private Texture2D texture;
        private Material material;
        private Renderer surface;
        private OriginalBitmapFont font;
        private Camera view;
        private SpriteLibrary sprites;
        private Color32[] atlas;
        private Vector2 pointer;
        private bool click;
        public void Initialize(Camera camera, SpriteLibrary library, OriginalBitmapFont originalFont)
        {
            view = camera; sprites = library; font = originalFont; atlas = library.atlas.GetPixels32();
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            material = new Material(library.material) { mainTexture = texture };
            material.SetFloat("_Indexed", 0); material.SetFloat("_ArenaFloor", 0);
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad); plane.name = "Original presentation surface";
            Destroy(plane.GetComponent<Collider>()); plane.transform.SetParent(transform, false);
            plane.transform.localScale = new Vector3(1.5f, 1.05f, 1);
            surface = plane.GetComponent<Renderer>(); surface.sharedMaterial = material;
            surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
            Position();
        }
        public void Position()
        {
            transform.SetPositionAndRotation(view.transform.position + view.transform.forward * 1.35f, view.transform.rotation);
        }
        public void Visible(bool visible) => surface.enabled = visible;
        public void Begin()
        {
            System.Array.Fill(pixels, new Color32(4, 7, 11, 255));
            pointer = new Vector2(-1, -1); click = false;
            if (Mouse.current == null) return;
            Ray ray = view.ScreenPointToRay(Mouse.current.position.ReadValue());
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
            pointer = new Vector2((local.x / 1.5f + 0.5f) * Width, (0.5f - local.y / 1.05f) * Height);
            click = Mouse.current.leftButton.wasPressedThisFrame;
        }
        public void End() { texture.SetPixels32(pixels); texture.Apply(false, false); }
        private void Pixel(int x, int y, Color32 color)
        {
            if ((uint)x < Width && (uint)y < Height) pixels[(Height - 1 - y) * Width + x] = color;
        }
        public void Text(string text, float x, float y, Color color, bool centered = false, int clipTop = 0, int clipBottom = Height)
        {
            foreach (string line in text.Split('\n'))
            {
                int left = Mathf.RoundToInt(centered ? x - font.Width(line) / 2f : x);
                foreach (char raw in line)
                {
                    char c = raw >= 32 && raw < 127 ? raw : '?';
                    for (int row = 0; row < 12; row++)
                    {
                        int yy = Mathf.RoundToInt(y) + row;
                        if (yy < clipTop || yy >= clipBottom) continue;
                        for (int column = 0; column < 16; column++) if (font.Pixel(c, column, row)) Pixel(left + column, yy, color);
                    }
                    left += font.Advance(c);
                }
                y += 13;
            }
        }
        public bool Button(string label, float x, float y, float width, bool selected)
        {
            bool hover = new Rect(x, y, width, 20).Contains(pointer);
            Color32 background = hover || selected ? new Color32(31, 66, 77, 255) : new Color32(10, 23, 28, 255);
            for (int row = (int)y; row < y + 20; row++) for (int column = (int)x; column < x + width; column++) Pixel(column, row, background);
            Text(label, x + width / 2, y + 4, hover || selected ? Color.yellow : Color.cyan, true);
            return hover && click;
        }
        public void Sprite(int offset, float x, float y, int scale, int bank = 0x20eb, int clipTop = 0, int clipBottom = Height)
        {
            SpriteGraphic sprite = sprites.Find(offset, bank);
            if (sprite == null) return;
            for (int row = 0; row < sprite.height; row++) for (int column = 0; column < sprite.width; column++)
            {
                Color32 color = atlas[(sprite.y + sprite.height - 1 - row) * sprites.atlas.width + sprite.x + column];
                if (color.a < 128) continue;
                for (int yy = 0; yy < scale; yy++) for (int xx = 0; xx < scale; xx++)
                {
                    int targetY = (int)y + row * scale + yy;
                    if (targetY >= clipTop && targetY < clipBottom) Pixel((int)x + column * scale + xx, targetY, color);
                }
            }
        }
        private void OnDestroy() { if (texture != null) Destroy(texture); if (material != null) Destroy(material); }
    }
}
