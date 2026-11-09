using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CarSales.Studio.EditorTools
{
    /// <summary>
    /// Asset Studio "Online" tab: search and download Sketchfab models (with the user's own API token) and
    /// Poly Haven skies and materials (CC0). Downloads go into Art/ and continue in the normal import steps.
    /// </summary>
    [Serializable]
    public class OnlinePanel
    {
        const string TokenKey = "CarSales.SketchfabToken";   // EditorPrefs: on this computer only, never in the project
        static readonly string[] Resolutions = { "1k", "2k", "4k" };

        public Action<string, AssetCategory, LicenceInfo> OnModelDownloaded;   // (asset path, category, licence)
        public Action<string> SetStatus;
        public Action Repaint;

        int source;               // 0 Sketchfab, 1 Poly Haven
        // Sketchfab
        string query = "car";
        bool carsOnly = true;
        AssetCategory downloadAs = AssetCategory.Vehicle;
        [NonSerialized] List<SketchfabModel> results = new List<SketchfabModel>();
        string next = "";
        // Poly Haven
        bool hdris = true;
        string filter = "";
        int resolution = 1;
        [NonSerialized] List<PolyHavenAsset> havenList;
        bool havenListIsHdri;
        [NonSerialized] Dictionary<string, Texture2D> thumbs;
        [NonSerialized] HashSet<string> thumbRequested;
        [NonSerialized] string busy;
        [NonSerialized] float progress;

        public void Draw()
        {
            // Not saved across script reloads: recreate.
            if (results == null) results = new List<SketchfabModel>();
            if (thumbs == null) thumbs = new Dictionary<string, Texture2D>();
            if (thumbRequested == null) thumbRequested = new HashSet<string>();
            source = GUILayout.Toolbar(source, new[] { "Sketchfab models", "Poly Haven skies & materials" }, GUILayout.Height(24));
            if (!string.IsNullOrEmpty(busy))
            {
                EditorGUILayout.HelpBox(busy, UnityEditor.MessageType.None);
                var r = GUILayoutUtility.GetRect(100f, 16f, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(r, progress, $"{progress:0%}");
            }
            if (source == 0) DrawSketchfab(); else DrawPolyHaven();
        }

        // ------------------------------------------------------------------ SKETCHFAB
        void DrawSketchfab()
        {
            EditorGUILayout.HelpBox("Search is free. Downloading needs your own Sketchfab API token (sketchfab.com → Settings → Password & API). " +
                                    "It is stored on this computer only. Only models the author made downloadable appear. Check each licence: " +
                                    "non-commercial (NC) models block release builds.", UnityEditor.MessageType.Info);
            string token = EditorPrefs.GetString(TokenKey, "");
            string newToken = EditorGUILayout.PasswordField("API token", token);
            if (newToken != token) EditorPrefs.SetString(TokenKey, newToken.Trim());
            using (new EditorGUILayout.HorizontalScope())
            {
                query = EditorGUILayout.TextField("Search", query);
                carsOnly = EditorGUILayout.ToggleLeft("Cars & vehicles only", carsOnly, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(EditorHttp.Busy && busy != null))
                    if (GUILayout.Button("Search", GUILayout.Width(80))) Search(SketchfabApi.SearchUrl(query, carsOnly), false);
            }
            downloadAs = (AssetCategory)EditorGUILayout.EnumPopup("Import as", downloadAs);

            foreach (var m in results)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(Thumb(m.ThumbnailUrl), GUILayout.Width(128), GUILayout.Height(72));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(m.Name, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField($"by {m.Author}  ·  {m.Faces:N0} triangles");
                        var verdict = LicencePolicy.Check(new LicenceInfo { licence = m.Licence, author = m.Author, purchaseRef = "n/a" });
                        var style = new GUIStyle(EditorStyles.label) { normal = { textColor = verdict.BlocksRelease ? new Color(1f, 0.6f, 0.2f) : new Color(0.4f, 0.85f, 0.4f) } };
                        EditorGUILayout.LabelField($"{(m.LicenceLabel.Length > 0 ? m.LicenceLabel : "Unknown licence")} - {verdict.Status}", style);
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(150)))
                    {
                        if (GUILayout.Button("Open page")) Application.OpenURL(m.ViewerUrl);
                        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(EditorPrefs.GetString(TokenKey, "")) || busy != null))
                            if (GUILayout.Button("Download & set up")) DownloadSketchfab(m);
                    }
                }
            }
            if (!string.IsNullOrEmpty(next) && GUILayout.Button("More results")) Search(next, true);
        }

        void Search(string url, bool append)
        {
            busy = "Searching Sketchfab...";
            EditorHttp.GetText(url, null, (json, error) =>
            {
                busy = null;
                if (error != null) { SetStatus?.Invoke("Sketchfab search failed: " + error); Repaint?.Invoke(); return; }
                try
                {
                    var page = SketchfabApi.ParseSearch(json);
                    if (!append || results == null) results = new List<SketchfabModel>();
                    results.AddRange(page.Models.Where(x => x.Downloadable));
                    next = page.Next;
                    SetStatus?.Invoke(results.Count == 0 ? "No downloadable models found." : "");
                }
                catch (FormatException e) { SetStatus?.Invoke("Unexpected answer from Sketchfab: " + e.Message); }
                Repaint?.Invoke();
            });
        }

        void DownloadSketchfab(SketchfabModel m)
        {
            string token = EditorPrefs.GetString(TokenKey, "");
            var headers = new Dictionary<string, string> { { "Authorization", SketchfabApi.AuthHeader(token) } };
            busy = $"Asking Sketchfab for '{m.Name}'...";
            progress = 0f;
            EditorHttp.GetText(SketchfabApi.DownloadUrl(m.Uid), headers, (json, error) =>
            {
                if (error != null) { Fail("Sketchfab refused the download: " + error); return; }
                string url;
                bool zip;
                try { url = SketchfabApi.ParseDownload(json, out zip); }
                catch (FormatException e) { Fail("Unexpected answer from Sketchfab: " + e.Message); return; }
                if (string.IsNullOrEmpty(url)) { Fail("Sketchfab offered no GLB/glTF download for this model."); return; }

                string safe = StudioNames.SafeName(m.Name);
                string folder = StudioPaths.AssetFolder(downloadAs, safe) + "/Source";
                string target = zip ? Path.Combine(Application.temporaryCachePath, safe + ".zip") : $"{folder}/{safe}.glb";
                busy = $"Downloading '{m.Name}'...";
                // The file URL is pre-signed: no Authorization header (it would be rejected).
                EditorHttp.Download(url, target, null, err =>
                {
                    if (err != null) { Fail("Download failed: " + err); return; }
                    string modelPath = target;
                    if (zip)
                    {
                        Directory.CreateDirectory(folder);
                        ZipFile.ExtractToDirectory(target, folder);
                        File.Delete(target);
                        modelPath = Directory.GetFiles(folder, "*.gltf", SearchOption.AllDirectories).FirstOrDefault()?.Replace('\\', '/');
                    }
                    busy = null;
                    AssetDatabase.Refresh();
                    if (modelPath == null) { Fail("The download contained no .gltf file."); return; }
                    var licence = new LicenceInfo
                    {
                        source = "Sketchfab", sourceUrl = m.ViewerUrl, author = m.Author, authorUrl = m.AuthorUrl,
                        licence = m.Licence, licenceUrl = LicencePolicy.DefaultUrl(m.Licence), dateAcquired = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                        purchaseRef = m.Licence == LicenceType.RoyaltyFreePurchased ? "Sketchfab download " + m.Uid : ""
                    };
                    OnModelDownloaded?.Invoke(modelPath, downloadAs, licence);
                    Repaint?.Invoke();
                }, p => { progress = p; Repaint?.Invoke(); });
            });
        }

        void Fail(string message)
        {
            busy = null;
            SetStatus?.Invoke(message);
            Repaint?.Invoke();
        }

        Texture2D Thumb(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (thumbs.TryGetValue(url, out var t)) return t;
            if (thumbRequested.Add(url)) EditorHttp.GetTexture(url, tex => { thumbs[url] = tex; Repaint?.Invoke(); });
            return null;
        }

        // ------------------------------------------------------------------ POLY HAVEN
        void DrawPolyHaven()
        {
            EditorGUILayout.HelpBox("Powered by Poly Haven (polyhaven.com): free CC0 skies (HDRIs) and materials, no account needed.", UnityEditor.MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool wantHdri = GUILayout.Toolbar(hdris ? 0 : 1, new[] { "Skies (HDRI)", "Materials" }) == 0;
                if (wantHdri != hdris) { hdris = wantHdri; havenList = null; }
                resolution = EditorGUILayout.Popup(resolution, Resolutions, GUILayout.Width(60));
                if (GUILayout.Button("Load list", GUILayout.Width(80)) || (havenList == null && busy == null && !EditorHttp.Busy)) LoadHavenList();
            }
            filter = EditorGUILayout.TextField("Filter", filter);
            if (havenList == null) return;
            var shown = havenList.Where(a => string.IsNullOrEmpty(filter) ||
                                             a.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             a.Categories.Any(c => c.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)).Take(40).ToList();
            EditorGUILayout.LabelField($"{shown.Count} of {havenList.Count} shown (use the filter, e.g. 'asphalt', 'road', 'urban', 'sunset')");
            foreach (var a in shown)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(Thumb(a.Thumbnail), GUILayout.Width(72), GUILayout.Height(72));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(a.Name, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(string.Join(", ", a.Categories.Take(4)) + (a.Authors.Length > 0 ? "  ·  by " + a.Authors : ""));
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(120)))
                    {
                        if (GUILayout.Button("Open page")) Application.OpenURL(PolyHavenApi.PageUrl(a.Id));
                        using (new EditorGUI.DisabledScope(busy != null))
                            if (GUILayout.Button("Download")) DownloadHaven(a);
                    }
                }
            }
        }

        static Dictionary<string, string> HavenHeaders => new Dictionary<string, string> { { "User-Agent", PolyHavenApi.UserAgent } };

        void LoadHavenList()
        {
            busy = "Loading Poly Haven list...";
            bool asked = hdris;
            EditorHttp.GetText(PolyHavenApi.ListUrl(asked), HavenHeaders, (json, error) =>
            {
                busy = null;
                if (error != null) { SetStatus?.Invoke("Poly Haven: " + error); havenList = new List<PolyHavenAsset>(); Repaint?.Invoke(); return; }
                try { havenList = PolyHavenApi.ParseList(json); havenListIsHdri = asked; }
                catch (FormatException e) { SetStatus?.Invoke("Unexpected answer from Poly Haven: " + e.Message); havenList = new List<PolyHavenAsset>(); }
                Repaint?.Invoke();
            });
        }

        void DownloadHaven(PolyHavenAsset a)
        {
            bool sky = havenListIsHdri;
            string res = Resolutions[resolution];
            busy = $"Asking Poly Haven for '{a.Name}'...";
            progress = 0f;
            EditorHttp.GetText(PolyHavenApi.FilesUrl(a.Id), HavenHeaders, (json, error) =>
            {
                if (error != null) { Fail("Poly Haven: " + error); return; }
                var licence = new LicenceInfo
                {
                    source = "Poly Haven", sourceUrl = PolyHavenApi.PageUrl(a.Id), author = a.Authors, licence = LicenceType.CC0,
                    licenceUrl = LicencePolicy.DefaultUrl(LicenceType.CC0), dateAcquired = DateTime.UtcNow.ToString("yyyy-MM-dd")
                };
                string safe = StudioNames.SafeName(a.Id);
                try
                {
                    if (sky)
                    {
                        string url = PolyHavenApi.HdriUrl(json, res);
                        if (url == null) { Fail("No .hdr file offered."); return; }
                        string folder = StudioPaths.AssetFolder(AssetCategory.Sky, safe);
                        string path = $"{folder}/{safe}_{res}.hdr";
                        busy = $"Downloading sky '{a.Name}' ({res})...";
                        EditorHttp.Download(url, path, HavenHeaders, err =>
                        {
                            if (err != null) { Fail("Download failed: " + err); return; }
                            AssetDatabase.Refresh();
                            var mat = AssetPipeline.CreateSky(folder, safe + "_Sky", path);
                            AssetPipeline.RegisterAsset(AssetCategory.Sky, safe, a.Name, mat, licence);
                            busy = null;
                            SetStatus?.Invoke($"Sky '{a.Name}' is ready: {AssetDatabase.GetAssetPath(mat)}. Library tab → 'Use as sky' puts it in the open scene.");
                            EditorGUIUtility.PingObject(mat);
                            Repaint?.Invoke();
                        }, p => { progress = p; Repaint?.Invoke(); });
                    }
                    else
                    {
                        var maps = PolyHavenApi.TextureUrls(json, res);
                        if (maps.Count == 0) { Fail("No texture maps offered."); return; }
                        string folder = StudioPaths.AssetFolder(AssetCategory.Material, safe);
                        var files = new Dictionary<TextureSlot, string>();
                        var queue = new Queue<KeyValuePair<TextureSlot, string>>(maps);
                        void NextMap()
                        {
                            if (queue.Count == 0)
                            {
                                    AssetDatabase.Refresh();
                                var mat = AssetPipeline.CreateMaterial(folder, safe, files);
                                AssetPipeline.RegisterAsset(AssetCategory.Material, safe, a.Name, mat, licence);
                                busy = null;
                                SetStatus?.Invoke($"Material '{a.Name}' is ready: {AssetDatabase.GetAssetPath(mat)} (drag it onto any object).");
                                EditorGUIUtility.PingObject(mat);
                                Repaint?.Invoke();
                                return;
                            }
                            var item = queue.Dequeue();
                            string ext = Path.GetExtension(new Uri(item.Value).AbsolutePath);
                            string path = $"{folder}/{safe}_{item.Key}{(string.IsNullOrEmpty(ext) ? ".jpg" : ext)}";
                            busy = $"Downloading '{a.Name}' {item.Key} ({res})...";
                            EditorHttp.Download(item.Value, path, HavenHeaders, err =>
                            {
                                if (err != null) { Fail($"Download of {item.Key} failed: {err}"); return; }
                                files[item.Key] = path;
                                NextMap();
                            }, p => { progress = p; Repaint?.Invoke(); });
                        }
                        NextMap();
                    }
                }
                catch (Exception e) { Debug.LogException(e); Fail("Poly Haven import failed: " + e.Message); }
            });
        }
    }
}
