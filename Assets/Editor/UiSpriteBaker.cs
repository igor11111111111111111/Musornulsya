#if UNITY_EDITOR
using System.IO;
using Musornulsya.Core;
using UnityEditor;
using UnityEngine;

namespace Musornulsya.EditorTools
{
    /// <summary>
    /// Печёт спрайты интерфейса в настоящие PNG-ассеты.
    ///
    /// Генерировать их в памяти нельзя: текстура, созданная в рантайме,
    /// не попадает в сохранённую сцену — спрайт остаётся, а пикселей нет,
    /// и вместо скруглений рисуется прямоугольник.
    ///
    /// Меню: Musornulsya > Испечь спрайты UI.
    /// </summary>
    public static class UiSpriteBaker
    {
        public const string Dir = "Assets/Resources/UiSprites";

        [MenuItem("Musornulsya/Испечь спрайты UI")]
        public static void Bake()
        {
            Directory.CreateDirectory(Dir);

            Save(UiSprites.BuildRoundedTexture(topLift: 0.16f, bottomDrop: 0.12f),
                UiSprites.RoundedName, UiSprites.Radius);

            Save(UiSprites.BuildRoundedTexture(topLift: 0.05f, bottomDrop: 0.05f),
                UiSprites.RoundedSoftName, UiSprites.Radius);

            Save(UiSprites.BuildCircleTexture(), UiSprites.CircleName, border: 0);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[UiSpriteBaker] Спрайты готовы: {Dir}");
        }

        private static void Save(Texture2D tex, string name, int border)
        {
            var path = $"{Dir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            // 9-slice: углы не тянутся, растягивается только середина.
            if (border > 0)
                importer.spriteBorder = new Vector4(border, border, border, border);

            importer.SaveAndReimport();
        }
    }
}
#endif
