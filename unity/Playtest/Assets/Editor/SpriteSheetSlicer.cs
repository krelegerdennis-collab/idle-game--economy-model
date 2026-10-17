using UnityEditor;
using UnityEngine;

namespace Playtest.Editor
{
    public static class SpriteSheetSlicer
    {
        const string SheetPath = "Assets/Sprites/Characters/assassin_sheet.png";

        // Frame counts and rates for this uneven sheet. Runtime playback reads the same
        // values from assassin.json. Another enemy sheet can call Slice with its own rects.
        const int RunFrameCount = 4;
        const int AttackFrameCount = 3;
        const int IdleFrameCount = 2;
        const float RunFps = 12f;
        const float AttackFps = 15f;
        const float IdleFps = 6f;

        static readonly string[] Names =
        {
            "assassin_run_0",
            "assassin_run_1",
            "assassin_run_2",
            "assassin_run_3",
            "assassin_attack_0",
            "assassin_attack_1",
            "assassin_attack_2",
            "assassin_idle_0",
            "assassin_idle_1",
        };

        // Unity sprite rects: origin is the bottom-left of assassin_sheet.png. Not a 3x3.
        static readonly Rect[] Rects =
        {
            new Rect(7f, 757f, 174f, 238f),
            new Rect(182f, 757f, 171f, 238f),
            new Rect(355f, 757f, 164f, 238f),
            new Rect(519f, 757f, 162f, 238f),
            new Rect(16f, 399f, 186f, 264f),
            new Rect(214f, 399f, 290f, 259f),
            new Rect(504f, 398f, 166f, 265f),
            new Rect(82f, 20f, 190f, 325f),
            new Rect(385f, 20f, 191f, 325f),
        };

        [MenuItem("Tools/Slice Assassin Sheet")]
        public static void SliceAssassinSheet()
        {
            Slice(SheetPath, Names, Rects);
            Debug.Log(
                "Sliced assassin sheet. Run "
                + RunFrameCount + " at " + RunFps
                + " fps, Attack " + AttackFrameCount + " at " + AttackFps
                + " fps, Idle " + IdleFrameCount + " at " + IdleFps + " fps.");
        }

        public static void Slice(string assetPath, string[] names, Rect[] rects)
        {
            if (string.IsNullOrEmpty(assetPath) || names == null || rects == null || names.Length != rects.Length || names.Length == 0)
            {
                Debug.LogError("Sprite sheet slice needs a path and one rect per name.");
                return;
            }
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("Missing sprite sheet at " + assetPath);
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.spritePixelsToUnits = 64f;
            importer.npotScale = TextureImporterNPOTScale.None;
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Multiple;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            settings.filterMode = FilterMode.Point;
            settings.alphaIsTransparency = true;
            settings.mipmapEnabled = false;
            settings.readable = false;
            importer.SetTextureSettings(settings);
            SpriteMetaData[] sprites = new SpriteMetaData[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                sprites[index] = new SpriteMetaData
                {
                    name = names[index],
                    rect = rects[index],
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                };
            }
            importer.spritesheet = sprites;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }
    }
}
