using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace CarSales.Studio.EditorTools
{
    /// <summary>Small asynchronous HTTP helper for editor windows (polled from EditorApplication.update, no coroutines).</summary>
    public static class EditorHttp
    {
        class Pending
        {
            public UnityWebRequest Request;
            public Action<UnityWebRequest> Done;
            public Action<float> Progress;
        }

        static readonly List<Pending> pending = new List<Pending>();
        public static bool Busy => pending.Count > 0;

        static void Start(UnityWebRequest req, IDictionary<string, string> headers, Action<UnityWebRequest> done, Action<float> progress = null)
        {
            if (headers != null) foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);
            req.timeout = 120;
            req.SendWebRequest();
            if (pending.Count == 0) EditorApplication.update += Poll;
            pending.Add(new Pending { Request = req, Done = done, Progress = progress });
        }

        static void Poll()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                p.Progress?.Invoke(p.Request.downloadProgress);
                if (!p.Request.isDone) continue;
                pending.RemoveAt(i);
                try { p.Done?.Invoke(p.Request); }
                catch (Exception e) { Debug.LogException(e); }
                finally { p.Request.Dispose(); }
            }
            if (pending.Count == 0) EditorApplication.update -= Poll;
        }

        static string Error(UnityWebRequest r)
        {
            if (r.result == UnityWebRequest.Result.Success) return null;
            if (r.responseCode == 401) return "Not authorised (401): check your API token.";
            if (r.responseCode == 403) return "Forbidden (403): this item can't be downloaded with your account.";
            if (r.responseCode == 404) return "Not found (404).";
            if (r.responseCode == 429) return "Too many requests (429): wait a minute and try again.";
            return $"{r.error} ({r.responseCode})";
        }

        /// <summary>GET text. Callback: (text, error) - error is null on success.</summary>
        public static void GetText(string url, IDictionary<string, string> headers, Action<string, string> done) =>
            Start(UnityWebRequest.Get(url), headers, r => done(Error(r) == null ? r.downloadHandler.text : null, Error(r)));

        /// <summary>Downloads to a file (folders are created). Callback: error or null.</summary>
        public static void Download(string url, string path, IDictionary<string, string> headers, Action<string> done, Action<float> progress = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET) { downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true } };
            Start(req, headers, r => done(Error(r)), progress);
        }

        /// <summary>Downloads an image (PNG/JPG) as a texture; null on failure.</summary>
        public static void GetTexture(string url, Action<Texture2D> done) =>
            Start(UnityWebRequestTexture.GetTexture(url), null, r => done(Error(r) == null ? DownloadHandlerTexture.GetContent(r) : null));
    }
}
