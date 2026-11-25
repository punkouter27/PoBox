using UnityEditor;
using UnityEngine;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Import settings for files that arrive later (2026-11-25; the project had no import presets at all, so the
    /// WebGL and Android builds carried whatever Unity's defaults were).
    ///
    /// Only assets that have never been imported are touched: a file without a .meta yet. On a re-import — which Unity
    /// runs for the whole project whenever a script changes — the .meta exists, so nothing already in `Assets/` is
    /// re-imported or changed by this file; it shapes what is added from now on, which is what a preset is for. The
    /// numbers are the ones this project's own assets carry (mono one-shots that decompress on load, textures
    /// compressed for the phone and the browser, models whose materials are authored in-repo rather than taken from
    /// the file).
    /// </summary>
    public sealed class ImportPresets : AssetPostprocessor
    {
        /// <summary>
        /// True only for a file that has never had import settings: <see cref="AssetImporter.importSettingsMissing"/> is
        /// exactly that (its .meta does not exist yet). A re-import — which Unity runs for the whole project whenever a
        /// script changes — has its .meta already, so nothing that ships today is touched.
        /// </summary>
        bool IsNew => assetImporter.importSettingsMissing;

        void OnPreprocessAudio()
        {
            if (!IsNew) return;
            var audio = (AudioImporter)assetImporter;
            AudioImporterSampleSettings s = audio.defaultSampleSettings;
            // Vorbis at a quality that keeps leather and brass; decompressed on load so a punch never hitches.
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.preloadAudioData = true;
            audio.defaultSampleSettings = s;
            audio.forceToMono = false;          // a stereo bed is a stereo bed
        }

        void OnPreprocessTexture()
        {
            if (!IsNew) return;
            var tex = (TextureImporter)assetImporter;
            tex.mipmapEnabled = true;
            tex.alphaIsTransparency = false;

            // The phone gets Astc, the browser Dxt: the two formats every target has in hardware.
            var android = tex.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.compressionQuality = 50;    // normal quality
            tex.SetPlatformTextureSettings(android);

            var webgl = tex.GetPlatformTextureSettings("WebGL");
            webgl.overridden = true;
            webgl.format = TextureImporterFormat.DXT5;
            tex.SetPlatformTextureSettings(webgl);
        }

        void OnPreprocessModel()
        {
            if (!IsNew) return;
            var model = (ModelImporter)assetImporter;
            // The prefabs and materials are authored in the project; a file's own materials would arrive as duplicates.
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.optimizeMeshPolygons = true;
            model.optimizeMeshVertices = true;
        }
    }
}
