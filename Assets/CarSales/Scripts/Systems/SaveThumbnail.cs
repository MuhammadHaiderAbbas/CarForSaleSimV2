using System;
using System.IO;
using UnityEngine;

namespace CarSales
{
    /// <summary>Writes / reads the small screenshot of a save slot (shown on the main menu's slot cards).</summary>
    public static class SaveThumbnail
    {
        public const int Width = 384, Height = 216;

        /// <summary>Renders <paramref name="cam"/> (without UI) into a PNG. Failures are logged, never thrown.</summary>
        public static void Capture(Camera cam, string path)
        {
            if (!cam || string.IsNullOrEmpty(path)) return;
            RenderTexture rt = null;
            Texture2D tex = null;
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(Width, Height, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply(false);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e) { Debug.LogWarning("[CarSales] Save thumbnail failed: " + e.Message); }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                if (rt) RenderTexture.ReleaseTemporary(rt);
                if (tex) UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>The thumbnail as a texture, or null. The caller destroys it.</summary>
        public static Texture2D Load(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "SaveThumb" };
                return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
            }
            catch (Exception) { return null; }
        }
    }
}
