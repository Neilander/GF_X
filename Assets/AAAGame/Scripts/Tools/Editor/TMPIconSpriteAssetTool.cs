using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public static class TMPIconSpriteAssetTool
{
    private const string TMPIconFolderPath = "Assets/AAAGame/Sprites/UI/TMPIcon";
    private const string TMPIconAtlasPath = "Assets/AAAGame/Sprites/UI/TMPIcon/TMPIcon.spriteatlas";
    private const string SpriteCloneSuffix = "(Clone)";
    private const float InlineIconScale = 0.8f;
    private const float InlineCenterRatioAboveBaseline = 0.3f;

    [MenuItem("Tools/TMPIcon/生成图集并覆盖TMP_SpriteAsset", priority = 100)]
    private static void GenerateTMPIconTmpSpriteAsset()
    {
        if (!AssetDatabase.IsValidFolder(TMPIconFolderPath))
        {
            GFBuiltin.LogError($"未找到目录: {TMPIconFolderPath}");
            return;
        }

        AssetDatabase.DeleteAsset(TMPIconAtlasPath);

        var atlas = new SpriteAtlas();
        atlas.SetPackingSettings(new SpriteAtlasPackingSettings
        {
            enableRotation = false,
            enableTightPacking = false,
            padding = 2,
            blockOffset = 1
        });
        atlas.SetTextureSettings(new SpriteAtlasTextureSettings
        {
            readable = false,
            generateMipMaps = false,
            sRGB = true,
            filterMode = FilterMode.Bilinear
        });

        AssetDatabase.CreateAsset(atlas, TMPIconAtlasPath);
        Object iconFolder = AssetDatabase.LoadAssetAtPath<Object>(TMPIconFolderPath);
        atlas.Add(new[] { iconFolder });
        EditorUtility.SetDirty(atlas);
        AssetDatabase.SaveAssets();

        SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget, false);
        GenerateTmpSpriteAsset(atlas);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        GFBuiltin.Log($"TMPIcon TMP_SpriteAsset 生成完成: {TMPIconFolderPath}/TMPIcon.asset");
    }

    private static void GenerateTmpSpriteAsset(SpriteAtlas atlas)
    {
        string srcFileName = AssetDatabase.GetAssetPath(atlas);
        string srcFileDir = Path.GetDirectoryName(srcFileName);
        string srcFileNameWithoutExtension = Path.GetFileNameWithoutExtension(srcFileName);
        string tmpSpriteAssetName = UtilityBuiltin.AssetsPath.GetCombinePath(srcFileDir, srcFileNameWithoutExtension + ".asset");
        string textureFileName = UtilityBuiltin.AssetsPath.GetCombinePath(srcFileDir, srcFileNameWithoutExtension + ".png");
        if (!SpriteAtlasToTexture(atlas, textureFileName))
        {
            return;
        }

        var getSpritesFunc = typeof(SpriteAtlasExtensions).GetMethod("GetPackedSprites", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Sprite[] sprites = getSpritesFunc.Invoke(null, new object[] { atlas }) as Sprite[];
        System.Array.Sort(sprites, (a, b) => a.name.CompareTo(b.name));

        TMP_SpriteAsset spriteAsset;
        if (File.Exists(tmpSpriteAssetName))
        {
            spriteAsset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(tmpSpriteAssetName);
        }
        else
        {
            spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            AssetDatabase.CreateAsset(spriteAsset, tmpSpriteAssetName);
        }

        spriteAsset.spriteSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFileName);
        spriteAsset.spriteCharacterTable.Clear();
        spriteAsset.spriteGlyphTable.Clear();
        if (spriteAsset.material == null)
        {
            Material material = new Material(Shader.Find("TextMeshPro/Sprite"));
            AssetDatabase.AddObjectToAsset(material, spriteAsset);
            AssetDatabase.SaveAssetIfDirty(spriteAsset);
            spriteAsset.material = material;
        }

        spriteAsset.material.mainTexture = spriteAsset.spriteSheet;
        EditorUtility.SetDirty(spriteAsset.material);

        for (int i = 0; i < sprites.Length; i++)
        {
            Sprite sp = sprites[i];
            Rect spUVRect = sp.textureRect;
            float renderWidth = spUVRect.width * InlineIconScale;
            float renderHeight = spUVRect.height * InlineIconScale;
            float centerY = spUVRect.height * InlineCenterRatioAboveBaseline;
            float horizontalBearingY = centerY + renderHeight * 0.5f;
            var glyph = new TMP_SpriteGlyph((uint)i,
                new UnityEngine.TextCore.GlyphMetrics(renderWidth, renderHeight, 0, horizontalBearingY, renderWidth),
                new UnityEngine.TextCore.GlyphRect(spUVRect), 1, 0);
            spriteAsset.spriteGlyphTable.Add(glyph);

            var spChar = new TMP_SpriteCharacter(ToUnicode(i.ToString()), glyph)
            {
                name = NormalizeSpriteName(sp.name)
            };
            spriteAsset.spriteCharacterTable.Add(spChar);
        }

        AssetDatabase.SaveAssetIfDirty(spriteAsset);
    }

    private static bool SpriteAtlasToTexture(SpriteAtlas atlas, string outputFile)
    {
        if (atlas == null || atlas.spriteCount == 0)
            return false;

        var getPreviewFunc = typeof(SpriteAtlasExtensions).GetMethod("GetPreviewTextures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (getPreviewFunc == null)
            return false;

        Texture2D[] previews = getPreviewFunc.Invoke(null, new object[] { atlas }) as Texture2D[];
        if (previews == null || previews.Length != 1)
        {
            GFBuiltin.LogError($"SpriteAtlas转换为TMP_Sprite失败: 图集存在{(previews == null ? 0 : previews.Length)}个子图集,请修改MaxTextureSize以确保为单图集");
            return false;
        }

        Texture2D atlasTex2d = previews[0];
        RenderTexture rt = new RenderTexture(atlasTex2d.width, atlasTex2d.height, 0);
        Graphics.Blit(atlasTex2d, rt);
        RenderTexture.active = rt;

        Texture2D readableAtlasTex = new Texture2D(rt.width, rt.height)
        {
            alphaIsTransparency = true
        };
        readableAtlasTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readableAtlasTex.Apply();
        RenderTexture.active = null;
        rt.Release();

        try
        {
            File.WriteAllBytes(outputFile, readableAtlasTex.EncodeToPNG());
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return false;
        }

        AssetDatabase.Refresh();
        TextureImporter texImporter = AssetImporter.GetAtPath(outputFile) as TextureImporter;
        texImporter.textureType = TextureImporterType.Default;
        texImporter.textureShape = TextureImporterShape.Texture2D;
        texImporter.alphaIsTransparency = true;
        texImporter.SaveAndReimport();
        return true;
    }

    private static string NormalizeSpriteName(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName))
            return string.Empty;

        if (spriteName.EndsWith(SpriteCloneSuffix))
            return spriteName[..^SpriteCloneSuffix.Length];

        return spriteName;
    }

    private static uint ToUnicode(string chars)
    {
        if (char.IsHighSurrogate(chars, 0) && 1 < chars.Length && char.IsLowSurrogate(chars, 1))
            return (uint)char.ConvertToUtf32(chars[0], chars[1]);

        return chars[0];
    }
}
