using UnityEditor;

namespace VRSurgery.EditorTools
{
    /// <summary>
    /// Import settings for the textures taken out of the .glb models by
    /// Tools/modelos/extrair_texturas_glb.py, decided by the role at the end of the file name.
    ///
    /// Embedded in the .glb, glTFast imported them uncompressed: the anatomical heart alone was
    /// 341 MB of the APK and of the headset's video memory. As files they go through the texture
    /// importer, so the Quest gets ASTC at most 1024 px — the heart is seen at arm's length.
    ///
    /// Applied only on a texture's first import, so a hand-tuned setting is never overwritten.
    /// </summary>
    public class GlbTextureImportRules : AssetPostprocessor
    {
        private const string Folder = "/Texturas/";
        private const int MaxSize = 1024;

        /// <summary>A flat colour (a uniform roughness or a blank normal map) compresses to a few hundred bytes.</summary>
        private const long FlatFileBytes = 4096;
        private const int FlatMaxSize = 64;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Models/") || !assetPath.Contains(Folder)) { return; }
            if (!assetImporter.importSettingsMissing) { return; }

            TextureImporter importer = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            if (name.EndsWith("_normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
            else if (name.EndsWith("_metallicRoughness") || name.EndsWith("_occlusion"))
            {
                // Data, not colour: read in linear space, as glTF defines them.
                importer.sRGBTexture = false;
            }

            // Several of the heart's maps are one flat colour stored at 2048 px: 21 MB each
            // uncompressed, for a picture that a 64 px texture shows identically.
            long bytes = new System.IO.FileInfo(assetPath).Length;
            int maxSize = bytes < FlatFileBytes ? FlatMaxSize : MaxSize;

            importer.maxTextureSize = maxSize;
            importer.mipmapEnabled = true;

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = maxSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
