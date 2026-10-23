using System;
using System.Collections.Generic;
using Llamatron.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llamatron.Unity
{
    public sealed class ArenaRenderer : MonoBehaviour
    {
        [Range(0, 30)] public float SpriteYawVariationDegrees = 12f;
        private sealed class Visual
        {
            public Transform transform;
            public MeshFilter filter;
            public MeshRenderer renderer;
        }

        public const float PixelX = 0.00625f, PixelY = 0.0075f;
        private SpriteLibrary library;
        private Visual[] visuals;
        private Color32[] atlasPixels;
        private readonly Color32[] effects = new Color32[320 * 200];
        private Texture2D effectTexture;
        private Renderer effectRenderer;
        private Material effectMaterial, floorMaterial, spriteMaterial;
        private Texture2D paletteTexture;
        private readonly Color32[] palettePixels = new Color32[16];
        private int paletteRevision = -1;
        private PaletteState appliedPalette;
        private readonly List<SpriteCommand> lastCommands = new List<SpriteCommand>(192);
        private bool wiping;
        private Texture2D floorTexture;
        private float floorTop;
        public Bounds ArenaBounds { get; private set; }

        public void Initialize(SpriteLibrary sprites)
        {
            library = sprites;
            if (library.indexAtlas == null) throw new InvalidOperationException("[LLPALETTE] Regenerate and bake the indexed sprite atlas.");
            atlasPixels = library.indexAtlas.GetPixels32();
            paletteTexture = new Texture2D(16, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            spriteMaterial = new Material(library.material) { mainTexture = library.indexAtlas };
            spriteMaterial.SetFloat("_Indexed", 1); spriteMaterial.SetTexture("_Palette", paletteTexture);
            floorTexture = new Texture2D(320, 200, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var grid = new Color32[320 * 200];
            for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++)
            {
                bool border = x < 2 || y < 2 || x > 317 || y > 197, line = x % 16 == 0 || y % 16 == 0;
                grid[y * 320 + x] = border ? new Color32(112, 183, 164, 255) : line ? new Color32(29, 55, 60, 255) : new Color32(16, 33, 38, 255);
            }
            floorTexture.SetPixels32(grid); floorTexture.Apply(false, true);
            floorMaterial = new Material(library.material) { mainTexture = floorTexture };
            floorMaterial.SetFloat("_ArenaFloor", 1);
            GameObject floor = Plane("Arena", floorMaterial, 0.65f);
            ArenaBounds = floor.GetComponent<Renderer>().bounds;
            floorTop = ArenaBounds.max.y;
            effectTexture = new Texture2D(320, 200, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            effectMaterial = new Material(spriteMaterial) { mainTexture = effectTexture };
            effectRenderer = Plane("Original pixel effects", effectMaterial, floorTop + 0.07f).GetComponent<Renderer>();
            visuals = new Visual[192];
            for (int i = 0; i < visuals.Length; i++)
            {
                var go = new GameObject($"Sprite {i}", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = spriteMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false; renderer.enabled = false;
                visuals[i] = new Visual { transform = go.transform, filter = go.GetComponent<MeshFilter>(), renderer = renderer };
            }
        }

        public void ApplyPalette(PaletteState palette)
        {
            if (ReferenceEquals(appliedPalette, palette) && paletteRevision == palette.Revision) return;
            appliedPalette = palette;
            paletteRevision = palette.Revision;
            for (int i = 0; i < 16; i++)
            {
                PaletteState.Color color = palette[i];
                palettePixels[i] = new Color32(Expand(color.Red), Expand(color.Green), Expand(color.Blue), 255);
            }
            paletteTexture.SetPixels32(palettePixels); paletteTexture.Apply(false, false);
            floorMaterial.SetColor("_Background", ((Color)palettePixels[0]).linear);
        }

        // Match VGA DAC bit expansion, preserving all six component bits.
        private static byte Expand(byte value) => (byte)((value << 2) | (value >> 4));

        private GameObject Plane(string label, Material material, float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = label;
            Destroy(go.GetComponent<Collider>()); go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, y, -1.75f);
            go.transform.localRotation = Quaternion.Euler(90, 180, 0); go.transform.localScale = new Vector3(2, 1.5f, 1);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        public void Present(SpriteFrame frame)
        {
            wiping = false;
            lastCommands.Clear(); lastCommands.AddRange(frame.Commands);
            Array.Clear(effects, 0, effects.Length);
            int count = 0;
            foreach (SpriteCommand command in frame.Commands)
            {
                if (command.IsLine) { RasterLine(command); continue; }
                SpriteGraphic graphic = library.Find(command.Offset);
                if (graphic == null) throw new InvalidOperationException($"[LLUNITY] Missing extracted sprite {command.Offset:x4}.");
                if (command.Transformed) { RasterRows(graphic, command); continue; }
                if (count == visuals.Length) throw new InvalidOperationException("[LLUNITY] Sprite command capacity exceeded.");
                Visual visual = visuals[count++]; visual.renderer.enabled = true; visual.filter.sharedMesh = graphic.mesh;
                float z = -2.5f + command.Y * PixelY;
                // The initial camera looks toward -Z, so screen-right is world -X.
                // Face sprite fronts toward +Z; use the same orientation for the effect plane.
                visual.transform.localPosition = new Vector3((160 - command.X) * PixelX,
                    floorTop + (command.Flat ? 0.07f : 0.008f), command.Flat ? z + graphic.height * PixelY / 2 : z);
                int yawKey = command.PresentationId != 0 ? command.PresentationId : count + 1000;
                visual.transform.localRotation = command.Flat ? Quaternion.Euler(90, 180, 0) : Quaternion.Euler(0, 180 + SpriteYaw(yawKey), 0);
                visual.transform.localScale = new Vector3(graphic.width * PixelX, graphic.height * PixelY, 1);
            }
            for (int i = count; i < visuals.Length; i++) visuals[i].renderer.enabled = false;
            effectTexture.SetPixels32(effects); effectTexture.Apply(false, false);
        }

        private float SpriteYaw(int key)
        {
            // Stable variation without consuming either gameplay or Unity random state.
            uint hash = unchecked((uint)key * 2654435761u);
            hash ^= hash >> 16;
            return ((hash & 65535u) / 65535f * 2 - 1) * SpriteYawVariationDegrees;
        }

        public void PresentWipe(WipeStep step)
        {
            if (!wiping)
            {
                wiping = true;
                Array.Clear(effects, 0, effects.Length);
                foreach (Visual visual in visuals) visual.renderer.enabled = false;
                // Retain the last drawn game frame as a flat indexed image while
                // the original 2x2-pixel rectangle writes replace its pixels.
                foreach (SpriteCommand command in lastCommands)
                {
                    if (command.IsLine) RasterLine(command);
                    else RasterRows(library.Find(command.Offset), command);
                }
            }
            void Block(int x, int y)
            {
                Color32 color = new Color32(step.ColorIndex, 0, 0, 255);
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++)
                    if (x + dx >= 0 && x + dx < 320 && y + dy >= 0 && y + dy < 200)
                        effects[(199 - y - dy) * 320 + x + dx] = color;
            }
            for (int x = step.Left; x <= step.Right; x += 2) { Block(x, step.Top); Block(x, step.Bottom); }
            for (int y = step.Top; y <= step.Bottom; y += 2) { Block(step.Left, y); Block(step.Right, y); }
            effectTexture.SetPixels32(effects); effectTexture.Apply(false, false);
        }

        private void RasterLine(SpriteCommand command)
        {
            Color32 color = new Color32(command.ColorIndex, 0, 0, 255);
            if (command.Y == command.EndY)
            {
                if (command.Y < 0 || command.Y >= 200) return;
                int begin = Math.Max(0, Math.Min((int)command.X, command.EndX));
                int end = Math.Min(319, Math.Max((int)command.X, command.EndX));
                for (int x = begin; x <= end; x++) effects[(199 - command.Y) * 320 + x] = color;
            }
            else if (command.X == command.EndX)
            {
                if (command.X < 0 || command.X >= 320) return;
                int begin = Math.Max(0, Math.Min((int)command.Y, command.EndY));
                int end = Math.Min(199, Math.Max((int)command.Y, command.EndY));
                for (int y = begin; y <= end; y++) effects[(199 - y) * 320 + command.X] = color;
            }
            else throw new NotSupportedException("[LLUNITY] Only recovered axis-aligned pixel lines are supported.");
        }

        // CS:e135 transforms individual 16-pixel rows. Wide sprites branch to
        // CS:63d2 after the transformed origin calculation and use unit row steps.
        private void RasterRows(SpriteGraphic graphic, SpriteCommand command)
        {
            int halfY = (graphic.height - 1) >> 1;
            int skewStep = command.Skew == 0 ? 0 : command.Skew > 0 ? command.RowSpacing : -command.RowSpacing;
            int left = command.X - ((graphic.width - 1) >> 1) - halfY * unchecked((sbyte)skewStep);
            int top = command.Y - halfY * unchecked((byte)command.RowSpacing);
            int rowStep = graphic.width > 16 ? 1 : command.RowSpacing;
            if (graphic.width > 16) skewStep = 0;
            for (int row = 0; row < graphic.height; row++)
            {
                int y = top + row * rowStep, x0 = left + row * skewStep;
                if (y > 199) break;
                if (y < 0) continue;
                int source = (graphic.y + graphic.height - 1 - row) * library.atlas.width + graphic.x;
                for (int x = 0; x < graphic.width; x++)
                {
                    int targetX = x0 + x;
                    Color32 pixel = atlasPixels[source + x];
                    if (pixel.a != 0 && targetX >= 0 && targetX < 320) effects[(199 - y) * 320 + targetX] = pixel;
                }
            }
        }

        private void OnDestroy()
        {
            if (effectTexture) Destroy(effectTexture);
            if (floorTexture) Destroy(floorTexture);
            if (effectMaterial) Destroy(effectMaterial);
            if (floorMaterial) Destroy(floorMaterial);
            if (spriteMaterial) Destroy(spriteMaterial);
            if (paletteTexture) Destroy(paletteTexture);
        }
    }
}
