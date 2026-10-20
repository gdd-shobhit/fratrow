// uitk-snapshot harness runner.
//
// This file is copied by tools/unity/uitk-snapshot/snapshot.py into a throwaway Unity "harness"
// project (default %LOCALAPPDATA%\FratRow\uitk-snapshot-harness\<unity version>). It is NOT part of
// the FratRow game project and must never be placed under the game's Assets/ folder.
//
// Flow: snapshot.py writes a job JSON and launches
//   Unity.exe -batchmode -projectPath <harness> -executeMethod FratRowTools.UitkSnapshot.UitkSnapshotRunner.Run
//             -uitkSnapshotJob <job.json> -logFile <log>
// Run() force-reimports the copied UXML/USS (so their import warnings land in the log every run),
// enters play mode, and for every shot builds PanelSettings(targetTexture = RenderTexture) + UIDocument,
// applies the fixture, waits a few frames, reads the RenderTexture back and writes a PNG.
// It always ends with EditorApplication.Exit (0 = all shots OK, 1 = a shot failed, 2 = bad job,
// 3 = unexpected exception, 4 = timeout) and writes result.json next to the job file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace FratRowTools.UitkSnapshot
{
    /// <summary>Batch-mode entry point that renders UXML screens to PNG files.</summary>
    [InitializeOnLoad]
    public static class UitkSnapshotRunner
    {
        internal const string Prefix = "[uitk-snapshot] ";
        const string JobKey = "FratRowTools.UitkSnapshot.JobPath";
        const string StartKey = "FratRowTools.UitkSnapshot.StartTicks";

        static Dictionary<string, object> s_Job;
        static List<object> s_Shots = new List<object>();
        static string s_ResultPath;
        static double s_TimeoutSeconds = 180;
        static DateTime s_StartUtc = DateTime.UtcNow;
        static bool s_Finished;
        static int s_ShotIndex;
        static ShotRun s_Current;
        static string s_Phase = "starting";
        static readonly List<object> s_ShotResults = new List<object>();
        static readonly List<string> s_Errors = new List<string>();
        static readonly List<string> s_Console = new List<string>();

        static UitkSnapshotRunner()
        {
            string jobPath = SessionState.GetString(JobKey, string.Empty);
            if (string.IsNullOrEmpty(jobPath))
                return;

            // Back after the domain reload that entering play mode triggers: resume the job.
            long ticks;
            s_StartUtc = long.TryParse(SessionState.GetString(StartKey, string.Empty), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out ticks)
                ? new DateTime(ticks, DateTimeKind.Utc)
                : DateTime.UtcNow;
            try
            {
                LoadJob(jobPath);
            }
            catch (Exception e)
            {
                s_Errors.Add("could not reload job after domain reload: " + e.Message);
                EditorApplication.delayCall += () => Finish(2);
                return;
            }
            s_Phase = "entering play mode";
            Hook();
        }

        /// <summary>-executeMethod entry point. Reads -uitkSnapshotJob &lt;path&gt;.</summary>
        public static void Run()
        {
            string jobPath = GetArg("-uitkSnapshotJob");
            if (string.IsNullOrEmpty(jobPath))
            {
                Debug.LogError(Prefix + "missing command line argument -uitkSnapshotJob <path>");
                EditorApplication.Exit(2);
                return;
            }

            try
            {
                LoadJob(jobPath);
            }
            catch (Exception e)
            {
                s_Errors.Add("could not read job '" + jobPath + "': " + e.Message);
                Finish(2);
                return;
            }

            try
            {
                Log("job " + jobPath + " with " + s_Shots.Count + " shot(s), Unity " + Application.unityVersion);
                ApplyColorSpace(GetString(s_Job, "colorSpace"));
                ReimportUiAssets(GetList(s_Job, "reimport"));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception e)
            {
                s_Errors.Add("setup failed: " + e);
                Finish(3);
                return;
            }

            // The play-mode timeout starts now, after any (re)import work.
            s_StartUtc = DateTime.UtcNow;
            SessionState.SetString(JobKey, jobPath);
            SessionState.SetString(StartKey, s_StartUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            Hook();
            s_Phase = "entering play mode";
            Log("entering play mode");
            EditorApplication.EnterPlaymode();
        }

        static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        static void LoadJob(string path)
        {
            s_ResultPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "result.json");
            var job = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (job == null)
                throw new FormatException("job file is not a JSON object");
            s_Job = job;
            s_Shots = GetList(job, "shots") ?? new List<object>();
            s_TimeoutSeconds = GetDouble(job, "timeoutSeconds", 180);
        }

        static void ApplyColorSpace(string wanted)
        {
            if (string.IsNullOrEmpty(wanted))
                return;
            ColorSpace cs = string.Equals(wanted, "Gamma", StringComparison.OrdinalIgnoreCase)
                ? ColorSpace.Gamma
                : ColorSpace.Linear;
            if (PlayerSettings.colorSpace == cs)
                return;
            Log("switching harness color space to " + cs + " to match the source project (one-time texture reimport)");
            PlayerSettings.colorSpace = cs;
            AssetDatabase.SaveAssets();
        }

        static void ReimportUiAssets(List<object> paths)
        {
            if (paths == null || paths.Count == 0)
                return;
            // Force-reimporting the (tiny) UXML/USS/TSS files makes their importer warnings appear in this
            // run's log even when the files did not change since the previous run.
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (object p in paths)
                {
                    string path = p as string;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static void Tick()
        {
            if (s_Finished)
                return;
            try
            {
                double elapsed = (DateTime.UtcNow - s_StartUtc).TotalSeconds;
                if (elapsed > s_TimeoutSeconds)
                {
                    s_Errors.Add("timed out after " + elapsed.ToString("0", CultureInfo.InvariantCulture) +
                                 "s while " + s_Phase);
                    Finish(4);
                    return;
                }

                if (!EditorApplication.isPlaying)
                    return; // still entering play mode

                if (s_Current == null)
                {
                    if (s_ShotIndex >= s_Shots.Count)
                    {
                        Finish(AnyShotFailed() ? 1 : 0);
                        return;
                    }
                    s_Phase = "rendering shot " + (s_ShotIndex + 1) + "/" + s_Shots.Count;
                    s_Current = new ShotRun(s_ShotIndex, s_Shots[s_ShotIndex] as Dictionary<string, object>);
                    s_Current.Setup();
                }

                if (s_Current.Step())
                    CompleteCurrentShot();
            }
            catch (Exception e)
            {
                if (s_Current != null)
                {
                    s_Current.Errors.Add("exception: " + e);
                    CompleteCurrentShot();
                }
                else
                {
                    s_Errors.Add("exception: " + e);
                    Finish(3);
                }
            }
        }

        static void CompleteCurrentShot()
        {
            ShotRun shot = s_Current;
            s_Current = null;
            s_ShotIndex++;
            try
            {
                shot.Teardown();
            }
            catch (Exception e)
            {
                shot.Warnings.Add("teardown: " + e.Message);
            }
            s_ShotResults.Add(shot.ToResult());
            Log("shot " + (shot.Index + 1) + (shot.Errors.Count == 0 ? " OK -> " + shot.OutPath : " FAILED"));
        }

        static bool AnyShotFailed()
        {
            foreach (object r in s_ShotResults)
            {
                var d = r as Dictionary<string, object>;
                if (d == null || !(d["ok"] is bool) || !(bool)d["ok"])
                    return true;
            }
            return s_ShotResults.Count < s_Shots.Count;
        }

        static void Finish(int code)
        {
            if (s_Finished)
                return;
            s_Finished = true;
            if (s_Current != null)
            {
                s_Current.Errors.Add("not finished: " + s_Phase);
                try { s_Current.Teardown(); } catch (Exception) { /* exiting anyway */ }
                s_ShotResults.Add(s_Current.ToResult());
                s_Current = null;
            }

            try
            {
                var result = new Dictionary<string, object>
                {
                    { "exitCode", code },
                    { "ok", code == 0 },
                    { "unityVersion", Application.unityVersion },
                    { "colorSpace", QualitySettings.activeColorSpace.ToString() },
                    { "graphicsDevice", SystemInfo.graphicsDeviceType.ToString() },
                    { "errors", s_Errors },
                    { "console", s_Console },
                    { "shots", s_ShotResults },
                };
                if (!string.IsNullOrEmpty(s_ResultPath))
                    File.WriteAllText(s_ResultPath, MiniJson.Serialize(result), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError(Prefix + "could not write result: " + e.Message);
            }

            SessionState.EraseString(JobKey);
            SessionState.EraseString(StartKey);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            foreach (string err in s_Errors)
                Log("ERROR " + err);
            Log("exit code " + code);
            EditorApplication.Exit(code);
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log || message == null || message.StartsWith(Prefix, StringComparison.Ordinal))
                return;
            string line = type + ": " + message;
            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
                line += " | " + FirstLines(stackTrace, 3);
            if (s_Current != null)
            {
                if (s_Current.Console.Count < 100)
                    s_Current.Console.Add(line);
            }
            else if (s_Console.Count < 200)
            {
                s_Console.Add(line);
            }
        }

        internal static void Log(string message)
        {
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", Prefix + message);
        }

        static string FirstLines(string text, int count)
        {
            string[] lines = text.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length && i < count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(lines[i].Trim());
            }
            return sb.ToString();
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        // ------------------------------------------------------------------ JSON accessors
        internal static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
                return null;
            return Json.ToStr(v);
        }

        internal static double GetDouble(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
                return fallback;
            double r;
            return Json.TryDouble(v, out r) ? r : fallback;
        }

        internal static int GetInt(Dictionary<string, object> d, string key, int fallback)
        {
            return (int)Math.Round(GetDouble(d, key, fallback));
        }

        internal static List<object> GetList(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v))
                return null;
            return v as List<object>;
        }

        internal static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v))
                return null;
            return v as Dictionary<string, object>;
        }

        // ------------------------------------------------------------------ one shot
        sealed class ShotRun
        {
            public readonly int Index;
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Console = new List<string>();
            public string OutPath;

            readonly Dictionary<string, object> m_Spec;
            readonly DateTime m_Created = DateTime.UtcNow;
            int m_Width = 1920;
            int m_Height = 1080;
            int m_Frames = 8;
            bool m_Transparent;
            bool m_Done;
            int m_Attempt;
            int m_StartFrame;
            DateTime m_WaitStart;
            Color32 m_Sentinel;
            string m_RenderPath = "player loop";
            RenderTexture m_Rt;
            PanelSettings m_Panel;
            GameObject m_DocGo;
            GameObject m_BgGo;
            Texture2D m_BgTex;
            UIDocument m_Doc;
            int m_ElementCount;

            public ShotRun(int index, Dictionary<string, object> spec)
            {
                Index = index;
                m_Spec = spec ?? new Dictionary<string, object>();
            }

            public void Setup()
            {
                OutPath = GetString(m_Spec, "out");
                m_Width = Mathf.Clamp(GetInt(m_Spec, "width", 1920), 16, 8192);
                m_Height = Mathf.Clamp(GetInt(m_Spec, "height", 1080), 16, 8192);
                m_Frames = Math.Max(2, GetInt(m_Spec, "frames", 8));
                if (string.IsNullOrEmpty(OutPath))
                {
                    Fail("shot has no \"out\" path");
                    return;
                }

                string uxmlPath = GetString(m_Spec, "uxml");
                var tree = string.IsNullOrEmpty(uxmlPath) ? null : AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                if (tree == null)
                {
                    Fail("could not load VisualTreeAsset '" + uxmlPath + "' (UXML import failed? see the UXML errors in the log)");
                    return;
                }

                m_Panel = CreatePanelSettings();
                Dictionary<string, object> bg = GetDict(m_Spec, "background");
                m_Transparent = bg != null && bg.ContainsKey("transparent") && Json.ToBool(bg["transparent"]);

                var desc = new RenderTextureDescriptor(m_Width, m_Height, RenderTextureFormat.ARGB32, 24)
                {
                    sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                    msaaSamples = 1,
                    useMipMap = false,
                };
                m_Rt = new RenderTexture(desc) { name = "UitkSnapshotTarget" };
                m_Rt.Create();
                m_Sentinel = ClearToSentinel(m_Rt);

                m_Panel.targetTexture = m_Rt;
                m_Panel.clearColor = true;
                m_Panel.colorClearValue = new Color(0f, 0f, 0f, 0f);
                m_Panel.clearDepthStencil = true;

                // Background lives in its own UIDocument (sorted below) so the screen's own tree is untouched.
                VisualElement bgElement = CreateBackground(bg);
                m_BgGo = new GameObject("UitkSnapshotBackground");
                m_BgGo.SetActive(false);
                var bgDoc = m_BgGo.AddComponent<UIDocument>();
                bgDoc.panelSettings = m_Panel;
                bgDoc.sortingOrder = -1000f;
                m_BgGo.SetActive(true);

                m_DocGo = new GameObject("UitkSnapshotDocument");
                m_DocGo.SetActive(false);
                m_Doc = m_DocGo.AddComponent<UIDocument>();
                m_Doc.panelSettings = m_Panel;
                m_Doc.sortingOrder = 0f;
                m_Doc.visualTreeAsset = tree;
                m_DocGo.SetActive(true);

                VisualElement root = m_Doc.rootVisualElement;
                if (root == null)
                {
                    Fail("UIDocument produced no root element");
                    return;
                }

                if (bgElement != null)
                {
                    if (bgDoc.rootVisualElement != null)
                        bgDoc.rootVisualElement.Add(bgElement);
                    else
                        root.Insert(0, bgElement);
                }

                Dictionary<string, object> fixture = GetDict(m_Spec, "fixture");
                if (fixture != null)
                    Fixture.Apply(root, fixture, Warnings);

                m_StartFrame = Time.frameCount;
                m_WaitStart = DateTime.UtcNow;
            }

            PanelSettings CreatePanelSettings()
            {
                PanelSettings panel = null;
                string psPath = GetString(m_Spec, "panelSettings");
                if (!string.IsNullOrEmpty(psPath))
                {
                    var source = AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath);
                    if (source == null)
                        Warnings.Add("could not load PanelSettings '" + psPath + "'; using ScaleWithScreenSize 1920x1080 defaults");
                    else
                        panel = Object.Instantiate(source);
                }

                if (panel == null)
                {
                    panel = ScriptableObject.CreateInstance<PanelSettings>();
                    panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    panel.referenceResolution = new Vector2Int(1920, 1080);
                    panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                    panel.match = 0f;
                }
                panel.name = "UitkSnapshotPanelSettings";

                string themePath = GetString(m_Spec, "theme");
                if (!string.IsNullOrEmpty(themePath))
                {
                    var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
                    if (theme == null)
                        Warnings.Add("could not load theme '" + themePath + "'");
                    else
                        panel.themeStyleSheet = theme;
                }
                if (panel.themeStyleSheet == null)
                    Warnings.Add("no theme style sheet: built-in control styles and the default font are missing");
                return panel;
            }

            VisualElement CreateBackground(Dictionary<string, object> bg)
            {
                if (bg == null || m_Transparent)
                    return null;
                var e = new VisualElement { name = "uitk-snapshot-background", pickingMode = PickingMode.Ignore };
                e.style.position = new StyleEnum<Position>(Position.Absolute);
                e.style.left = new StyleLength(0f);
                e.style.top = new StyleLength(0f);
                e.style.right = new StyleLength(0f);
                e.style.bottom = new StyleLength(0f);

                string color = GetString(bg, "color");
                Color c;
                if (!string.IsNullOrEmpty(color))
                {
                    if (Styles.TryColor(color, out c))
                        e.style.backgroundColor = new StyleColor(c);
                    else
                        Warnings.Add("background color '" + color + "' not understood");
                }

                string image = GetString(bg, "image");
                if (!string.IsNullOrEmpty(image))
                {
                    try
                    {
                        m_BgTex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { name = "UitkSnapshotBackground" };
                        if (!m_BgTex.LoadImage(File.ReadAllBytes(image)))
                            throw new IOException("not a PNG/JPG");
                        m_BgTex.wrapMode = TextureWrapMode.Clamp;
                        e.style.backgroundImage = new StyleBackground(m_BgTex);
                        string fit = (GetString(bg, "fit") ?? "cover").ToLowerInvariant();
                        if (fit == "stretch")
                            e.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
                        else
                            e.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(fit == "contain" ? BackgroundSizeType.Contain : BackgroundSizeType.Cover));
                        e.style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
                        e.style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
                        e.style.backgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat));
                    }
                    catch (Exception ex)
                    {
                        Warnings.Add("background image '" + image + "' could not be loaded: " + ex.Message);
                    }
                }
                return e;
            }

            /// <summary>Advances the shot; returns true when it is finished (success or failure).</summary>
            public bool Step()
            {
                if (m_Done)
                    return true;
                if (Time.frameCount - m_StartFrame < m_Frames || (DateTime.UtcNow - m_WaitStart).TotalSeconds < 0.25)
                    return false;

                Texture2D tex = ReadBack();
                try
                {
                    if (IsUntouched(tex))
                    {
                        if (m_Attempt == 0)
                        {
                            // The player loop did not repaint the offscreen panel; poke UI Toolkit directly.
                            m_Attempt = 1;
                            m_RenderPath = ForceRepaint();
                            m_StartFrame = Time.frameCount;
                            m_WaitStart = DateTime.UtcNow;
                            return false;
                        }
                        Fail("UI Toolkit never rendered into the RenderTexture (render path: " + m_RenderPath + ")");
                        return true;
                    }

                    if (!m_Transparent)
                        ForceOpaque(tex);
                    string dir = Path.GetDirectoryName(Path.GetFullPath(OutPath));
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(OutPath, tex.EncodeToPNG());
                    if (m_Doc != null && m_Doc.rootVisualElement != null)
                        m_ElementCount = CountElements(m_Doc.rootVisualElement);
                    m_Done = true;
                    return true;
                }
                finally
                {
                    Object.DestroyImmediate(tex);
                }
            }

            string ForceRepaint()
            {
                Type util = typeof(UIDocument).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
                if (util == null)
                    return "forced repaint unavailable (UIElementsRuntimeUtility not found)";
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var used = new List<string>();
                foreach (string name in new[] { "UpdateRuntimePanels", "UpdatePanels", "RepaintOffscreenPanels" })
                {
                    MethodInfo m = util.GetMethod(name, flags, null, Type.EmptyTypes, null);
                    if (m == null)
                        continue;
                    m.Invoke(null, null);
                    used.Add(name);
                }
                if (used.Count == 0)
                {
                    MethodInfo m = util.GetMethod("RepaintPanels", flags, null, new[] { typeof(bool) }, null);
                    if (m != null)
                    {
                        m.Invoke(null, new object[] { true });
                        used.Add("RepaintPanels(true)");
                    }
                }
                string path = used.Count == 0 ? "forced repaint found no entry point" : "forced " + string.Join("+", used.ToArray());
                Warnings.Add("panel was not repainted by the player loop; used " + path);
                return path;
            }

            Texture2D ReadBack()
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = m_Rt;
                var tex = new Texture2D(m_Width, m_Height, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, m_Width, m_Height), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = previous;
                return tex;
            }

            static Color32 ClearToSentinel(RenderTexture rt)
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                GL.Clear(true, true, new Color(1f, 0f, 1f, 0.5f));
                var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                probe.Apply(false);
                Color32 value = probe.GetPixels32()[0];
                Object.DestroyImmediate(probe);
                RenderTexture.active = previous;
                return value;
            }

            bool IsUntouched(Texture2D tex)
            {
                Color32[] px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 p = px[i];
                    if (p.r != m_Sentinel.r || p.g != m_Sentinel.g || p.b != m_Sentinel.b || p.a != m_Sentinel.a)
                        return false;
                }
                return true;
            }

            static void ForceOpaque(Texture2D tex)
            {
                Color32[] px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                    px[i].a = 255;
                tex.SetPixels32(px);
                tex.Apply(false);
            }

            static int CountElements(VisualElement e)
            {
                int n = 1;
                foreach (VisualElement child in e.hierarchy.Children())
                    n += CountElements(child);
                return n;
            }

            void Fail(string message)
            {
                Errors.Add(message);
                m_Done = true;
            }

            public void Teardown()
            {
                if (m_DocGo != null) Object.DestroyImmediate(m_DocGo);
                if (m_BgGo != null) Object.DestroyImmediate(m_BgGo);
                if (m_Panel != null)
                {
                    m_Panel.targetTexture = null;
                    Object.DestroyImmediate(m_Panel);
                }
                if (m_Rt != null)
                {
                    m_Rt.Release();
                    Object.DestroyImmediate(m_Rt);
                }
                if (m_BgTex != null) Object.DestroyImmediate(m_BgTex);
                m_DocGo = m_BgGo = null;
                m_Panel = null;
                m_Rt = null;
                m_BgTex = null;
            }

            public Dictionary<string, object> ToResult()
            {
                return new Dictionary<string, object>
                {
                    { "index", Index },
                    { "out", OutPath },
                    { "ok", Errors.Count == 0 && m_Done },
                    { "width", m_Width },
                    { "height", m_Height },
                    { "elements", m_ElementCount },
                    { "renderPath", m_RenderPath },
                    { "seconds", Math.Round((DateTime.UtcNow - m_Created).TotalSeconds, 2) },
                    { "errors", Errors },
                    { "warnings", Warnings },
                    { "console", Console },
                };
            }
        }
    }

    // ====================================================================== fixtures
    /// <summary>Applies fixture "ops" (see tools/unity/uitk-snapshot/README.md) to a visual tree.</summary>
    static class Fixture
    {
        static readonly HashSet<string> s_PropKeys = new HashSet<string>
        {
            "text", "value", "label", "choices", "placeholder", "tooltip", "enabled", "display", "visible",
            "style", "class", "addClass", "removeClass", "toggleClass", "clear", "append", "children", "remove", "props",
        };
        static readonly HashSet<string> s_OpOnlyKeys = new HashSet<string> { "name", "withClass", "all" };
        static readonly HashSet<string> s_SpecOnlyKeys = new HashSet<string> { "type", "name" };

        public static void Apply(VisualElement root, Dictionary<string, object> fixture, List<string> warnings)
        {
            foreach (string key in fixture.Keys)
            {
                if (key != "ops" && !IsComment(key))
                    warnings.Add("fixture: unknown top-level key '" + key + "' (expected \"ops\")");
            }
            List<object> ops = UitkSnapshotRunner.GetList(fixture, "ops");
            if (ops == null)
                return;

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i] as Dictionary<string, object>;
                string where = "ops[" + i + "]";
                if (op == null)
                {
                    warnings.Add(where + ": not a JSON object");
                    continue;
                }
                string name = UitkSnapshotRunner.GetString(op, "name");
                string withClass = UitkSnapshotRunner.GetString(op, "withClass");
                where += " " + (name ?? ("." + withClass));
                if (name == null && withClass == null)
                {
                    warnings.Add(where + ": needs \"name\" (or \"withClass\")");
                    continue;
                }
                WarnUnknownKeys(op, s_OpOnlyKeys, where, warnings);

                var targets = new List<VisualElement>();
                if (op.ContainsKey("all") && Json.ToBool(op["all"]))
                {
                    root.Query(name, withClass).ForEach(targets.Add);
                }
                else
                {
                    VisualElement single = root.Q(name, withClass);
                    if (single != null)
                        targets.Add(single);
                }
                if (targets.Count == 0)
                {
                    warnings.Add(where + ": no element named '" + (name ?? "") + "'" +
                                 (withClass != null ? " with class '" + withClass + "'" : "") + " in the tree");
                    continue;
                }
                foreach (VisualElement target in targets)
                    ApplyProps(target, op, where, warnings);
            }
        }

        static bool IsComment(string key)
        {
            return key.StartsWith("_", StringComparison.Ordinal) || key == "comment" || key == "$schema";
        }

        static void WarnUnknownKeys(Dictionary<string, object> d, HashSet<string> extra, string where, List<string> warnings)
        {
            foreach (string key in d.Keys)
            {
                if (!s_PropKeys.Contains(key) && !extra.Contains(key) && !IsComment(key))
                    warnings.Add(where + ": unknown key '" + key + "' ignored");
            }
        }

        static void ApplyProps(VisualElement e, Dictionary<string, object> p, string where, List<string> warnings)
        {
            object v;
            if (p.TryGetValue("remove", out v) && Json.ToBool(v))
            {
                e.RemoveFromHierarchy();
                return;
            }
            if (p.TryGetValue("clear", out v) && Json.ToBool(v))
                e.Clear();
            if (p.TryGetValue("props", out v))
            {
                var props = v as Dictionary<string, object>;
                if (props == null)
                    warnings.Add(where + ": \"props\" must be an object");
                else
                    foreach (KeyValuePair<string, object> kv in props)
                        Reflect.SetProperty(e, kv.Key, kv.Value, where, warnings);
            }
            if (p.TryGetValue("label", out v))
                Reflect.SetProperty(e, "label", v, where, warnings);
            if (p.TryGetValue("choices", out v))
                Reflect.SetProperty(e, "choices", v, where, warnings);
            if (p.TryGetValue("text", out v))
                SetText(e, Json.ToStr(v), where, warnings);
            if (p.TryGetValue("value", out v))
                SetValue(e, v, where, warnings);
            if (p.TryGetValue("placeholder", out v))
                SetPlaceholder(e, Json.ToStr(v), where, warnings);
            if (p.TryGetValue("tooltip", out v))
                e.tooltip = Json.ToStr(v);
            if (p.TryGetValue("class", out v))
                foreach (string c in Json.ToStringList(v)) e.AddToClassList(c);
            if (p.TryGetValue("addClass", out v))
                foreach (string c in Json.ToStringList(v)) e.AddToClassList(c);
            if (p.TryGetValue("removeClass", out v))
                foreach (string c in Json.ToStringList(v)) e.RemoveFromClassList(c);
            if (p.TryGetValue("toggleClass", out v))
                foreach (string c in Json.ToStringList(v)) e.ToggleInClassList(c);
            if (p.TryGetValue("style", out v))
            {
                var style = v as Dictionary<string, object>;
                if (style == null)
                    warnings.Add(where + ": \"style\" must be an object");
                else
                    Styles.Apply(e, style, where, warnings);
            }
            if (p.TryGetValue("display", out v))
                Styles.Apply(e, new Dictionary<string, object> { { "display", v } }, where, warnings);
            if (p.TryGetValue("visible", out v))
                e.style.visibility = new StyleEnum<Visibility>(Json.ToBool(v) ? Visibility.Visible : Visibility.Hidden);
            if (p.TryGetValue("enabled", out v))
                e.SetEnabled(Json.ToBool(v));

            AppendChildren(e, p, "append", where, warnings);
            AppendChildren(e, p, "children", where, warnings);
        }

        static void AppendChildren(VisualElement parent, Dictionary<string, object> p, string key, string where, List<string> warnings)
        {
            object v;
            if (!p.TryGetValue(key, out v))
                return;
            var items = v as List<object>;
            if (items == null)
            {
                warnings.Add(where + ": \"" + key + "\" must be an array");
                return;
            }
            for (int i = 0; i < items.Count; i++)
            {
                VisualElement child = Create(items[i], where + "." + key + "[" + i + "]", warnings);
                if (child != null)
                    parent.Add(child);
            }
        }

        static VisualElement Create(object item, string where, List<string> warnings)
        {
            string shorthand = item as string;
            if (shorthand != null)
                return new Label(shorthand);
            var spec = item as Dictionary<string, object>;
            if (spec == null)
            {
                warnings.Add(where + ": element spec must be an object or a string");
                return null;
            }
            string typeName = UitkSnapshotRunner.GetString(spec, "type") ?? "VisualElement";
            VisualElement e = Instantiate(typeName);
            if (e == null)
            {
                warnings.Add(where + ": unknown element type '" + typeName + "', using VisualElement");
                e = new VisualElement();
            }
            WarnUnknownKeys(spec, s_SpecOnlyKeys, where, warnings);
            string name = UitkSnapshotRunner.GetString(spec, "name");
            if (!string.IsNullOrEmpty(name))
                e.name = name;
            ApplyProps(e, spec, where + " " + typeName, warnings);
            return e;
        }

        static VisualElement Instantiate(string typeName)
        {
            string t = typeName.Trim();
            int colon = t.IndexOf(':');
            if (colon >= 0)
                t = t.Substring(colon + 1);
            const string ns = "UnityEngine.UIElements.";
            if (t.StartsWith(ns, StringComparison.Ordinal))
                t = t.Substring(ns.Length);
            Type type = typeof(VisualElement).Assembly.GetType(ns + t, false);
            if (type == null || !typeof(VisualElement).IsAssignableFrom(type) || type.IsAbstract ||
                type.GetConstructor(Type.EmptyTypes) == null)
                return null;
            return (VisualElement)Activator.CreateInstance(type);
        }

        static void SetText(VisualElement e, string text, string where, List<string> warnings)
        {
            var te = e as TextElement;
            if (te != null) { te.text = text; return; }
            var foldout = e as Foldout;
            if (foldout != null) { foldout.text = text; return; }
            var sv = e as INotifyValueChanged<string>;
            if (sv != null) { sv.SetValueWithoutNotify(text); return; }
            if (Reflect.TrySet(e, "title", text) || Reflect.TrySet(e, "text", text))
                return;
            warnings.Add(where + ": " + e.GetType().Name + " has no settable text");
        }

        static void SetValue(VisualElement e, object v, string where, List<string> warnings)
        {
            double d;
            var s = e as INotifyValueChanged<string>;
            if (s != null) { s.SetValueWithoutNotify(Json.ToStr(v)); return; }
            var b = e as INotifyValueChanged<bool>;
            if (b != null) { b.SetValueWithoutNotify(Json.ToBool(v)); return; }
            if (Json.TryDouble(v, out d))
            {
                var f = e as INotifyValueChanged<float>;
                if (f != null) { f.SetValueWithoutNotify((float)d); return; }
                var i = e as INotifyValueChanged<int>;
                if (i != null) { i.SetValueWithoutNotify((int)Math.Round(d)); return; }
                var dbl = e as INotifyValueChanged<double>;
                if (dbl != null) { dbl.SetValueWithoutNotify(d); return; }
                var l = e as INotifyValueChanged<long>;
                if (l != null) { l.SetValueWithoutNotify((long)Math.Round(d)); return; }
            }
            warnings.Add(where + ": cannot set value '" + Json.ToStr(v) + "' on " + e.GetType().Name);
        }

        static void SetPlaceholder(VisualElement e, string text, string where, List<string> warnings)
        {
            PropertyInfo editionProp = e.GetType().GetProperty("textEdition", BindingFlags.Instance | BindingFlags.Public);
            object edition = editionProp != null ? editionProp.GetValue(e, null) : null;
            if (edition != null && Reflect.TrySet(edition, "placeholder", text))
                return;
            warnings.Add(where + ": " + e.GetType().Name + " has no placeholder");
        }
    }

    // ====================================================================== inline styles
    static class Styles
    {
        public static void Apply(VisualElement e, Dictionary<string, object> style, string where, List<string> warnings)
        {
            IStyle s = e.style;
            foreach (KeyValuePair<string, object> kv in style)
            {
                string key = kv.Key.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
                object v = kv.Value;
                bool ok = true;
                switch (key)
                {
                    case "width": ok = Len(v, x => s.width = x); break;
                    case "height": ok = Len(v, x => s.height = x); break;
                    case "minwidth": ok = Len(v, x => s.minWidth = x); break;
                    case "maxwidth": ok = Len(v, x => s.maxWidth = x); break;
                    case "minheight": ok = Len(v, x => s.minHeight = x); break;
                    case "maxheight": ok = Len(v, x => s.maxHeight = x); break;
                    case "left": ok = Len(v, x => s.left = x); break;
                    case "top": ok = Len(v, x => s.top = x); break;
                    case "right": ok = Len(v, x => s.right = x); break;
                    case "bottom": ok = Len(v, x => s.bottom = x); break;
                    case "margin": ok = Len(v, x => { s.marginLeft = x; s.marginTop = x; s.marginRight = x; s.marginBottom = x; }); break;
                    case "marginleft": ok = Len(v, x => s.marginLeft = x); break;
                    case "margintop": ok = Len(v, x => s.marginTop = x); break;
                    case "marginright": ok = Len(v, x => s.marginRight = x); break;
                    case "marginbottom": ok = Len(v, x => s.marginBottom = x); break;
                    case "padding": ok = Len(v, x => { s.paddingLeft = x; s.paddingTop = x; s.paddingRight = x; s.paddingBottom = x; }); break;
                    case "paddingleft": ok = Len(v, x => s.paddingLeft = x); break;
                    case "paddingtop": ok = Len(v, x => s.paddingTop = x); break;
                    case "paddingright": ok = Len(v, x => s.paddingRight = x); break;
                    case "paddingbottom": ok = Len(v, x => s.paddingBottom = x); break;
                    case "flexbasis": ok = Len(v, x => s.flexBasis = x); break;
                    case "fontsize": ok = Len(v, x => s.fontSize = x); break;
                    case "letterspacing": ok = Len(v, x => s.letterSpacing = x); break;
                    case "borderradius": ok = Len(v, x => { s.borderTopLeftRadius = x; s.borderTopRightRadius = x; s.borderBottomLeftRadius = x; s.borderBottomRightRadius = x; }); break;
                    case "flexgrow": ok = Num(v, x => s.flexGrow = x); break;
                    case "flexshrink": ok = Num(v, x => s.flexShrink = x); break;
                    case "opacity": ok = Num(v, x => s.opacity = x); break;
                    case "borderwidth": ok = Num(v, x => { s.borderLeftWidth = x; s.borderTopWidth = x; s.borderRightWidth = x; s.borderBottomWidth = x; }); break;
                    case "color": ok = Col(v, x => s.color = x); break;
                    case "backgroundcolor": ok = Col(v, x => s.backgroundColor = x); break;
                    case "unitybackgroundimagetintcolor": ok = Col(v, x => s.unityBackgroundImageTintColor = x); break;
                    case "bordercolor": ok = Col(v, x => { s.borderLeftColor = x; s.borderTopColor = x; s.borderRightColor = x; s.borderBottomColor = x; }); break;
                    case "display": ok = En<DisplayStyle>(v, x => s.display = x); break;
                    case "visibility": ok = En<Visibility>(v, x => s.visibility = x); break;
                    case "position": ok = En<Position>(v, x => s.position = x); break;
                    case "overflow": ok = En<Overflow>(v, x => s.overflow = x); break;
                    case "flexdirection": ok = En<FlexDirection>(v, x => s.flexDirection = x); break;
                    case "flexwrap": ok = En<Wrap>(v, x => s.flexWrap = x); break;
                    case "justifycontent": ok = En<Justify>(v, x => s.justifyContent = x); break;
                    case "alignitems": ok = En<Align>(v, x => s.alignItems = x); break;
                    case "alignself": ok = En<Align>(v, x => s.alignSelf = x); break;
                    case "aligncontent": ok = En<Align>(v, x => s.alignContent = x); break;
                    case "whitespace": ok = En<WhiteSpace>(v, x => s.whiteSpace = x); break;
                    case "textoverflow": ok = En<TextOverflow>(v, x => s.textOverflow = x); break;
                    case "unitytextalign":
                    case "textalign": ok = En<TextAnchor>(v, x => s.unityTextAlign = x); break;
                    case "unityfontstyle":
                    case "unityfontstyleandweight":
                    case "fontstyle": ok = En<FontStyle>(v, x => s.unityFontStyleAndWeight = x); break;
                    default:
                        warnings.Add(where + ": style property '" + kv.Key + "' is not supported by fixtures");
                        continue;
                }
                if (!ok)
                    warnings.Add(where + ": could not parse style " + kv.Key + ": '" + Json.ToStr(v) + "'");
            }
        }

        /// <summary>Maps null / "auto" / "none" / "initial" / "unset" to a StyleKeyword.</summary>
        static bool Keyword(object v, out StyleKeyword keyword)
        {
            keyword = StyleKeyword.Null;
            if (v == null)
                return true;
            string s = v as string;
            if (s == null)
                return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": keyword = StyleKeyword.Auto; return true;
                case "none": keyword = StyleKeyword.None; return true;
                case "initial": keyword = StyleKeyword.Initial; return true;
                case "null":
                case "unset":
                case "": keyword = StyleKeyword.Null; return true;
            }
            return false;
        }

        static bool Len(object v, Action<StyleLength> set)
        {
            StyleKeyword kw;
            if (Keyword(v, out kw)) { set(new StyleLength(kw)); return true; }
            Length length;
            if (!TryLength(v, out length)) return false;
            set(new StyleLength(length));
            return true;
        }

        static bool Num(object v, Action<StyleFloat> set)
        {
            StyleKeyword kw;
            if (Keyword(v, out kw)) { set(new StyleFloat(kw)); return true; }
            double d;
            string s = Json.ToStr(v).Trim();
            if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 2);
            if (!Json.TryDouble(s, out d)) return false;
            set(new StyleFloat((float)d));
            return true;
        }

        static bool Col(object v, Action<StyleColor> set)
        {
            StyleKeyword kw;
            string s = Json.ToStr(v).Trim().ToLowerInvariant();
            if ((v == null || s == "initial" || s == "null" || s == "unset") && Keyword(v, out kw)) { set(new StyleColor(kw)); return true; }
            Color c;
            if (!TryColor(Json.ToStr(v), out c)) return false;
            set(new StyleColor(c));
            return true;
        }

        static bool En<T>(object v, Action<StyleEnum<T>> set) where T : struct, IConvertible
        {
            string s = Json.ToStr(v).Trim().Replace("-", string.Empty).Replace("_", string.Empty);
            if (s.Equals("initial", StringComparison.OrdinalIgnoreCase)) { set(new StyleEnum<T>(StyleKeyword.Initial)); return true; }
            if (s.Equals("null", StringComparison.OrdinalIgnoreCase) || s.Equals("unset", StringComparison.OrdinalIgnoreCase)) { set(new StyleEnum<T>(StyleKeyword.Null)); return true; }
            if (typeof(T) == typeof(DisplayStyle) && v is bool)
                s = (bool)v ? "Flex" : "None";
            foreach (string n in Enum.GetNames(typeof(T)))
            {
                if (string.Equals(n, s, StringComparison.OrdinalIgnoreCase))
                {
                    set(new StyleEnum<T>((T)Enum.Parse(typeof(T), n)));
                    return true;
                }
            }
            return false;
        }

        public static bool TryLength(object v, out Length length)
        {
            length = default(Length);
            double d;
            if (v is double)
            {
                length = new Length((float)(double)v, LengthUnit.Pixel);
                return true;
            }
            string s = Json.ToStr(v).Trim().ToLowerInvariant();
            LengthUnit unit = LengthUnit.Pixel;
            if (s.EndsWith("%")) { unit = LengthUnit.Percent; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("px")) { s = s.Substring(0, s.Length - 2); }
            if (!Json.TryDouble(s.Trim(), out d)) return false;
            length = new Length((float)d, unit);
            return true;
        }

        public static bool TryColor(string text, out Color color)
        {
            color = Color.clear;
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.Trim();
            if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                int open = s.IndexOf('('), close = s.LastIndexOf(')');
                if (open < 0 || close <= open) return false;
                string[] parts = s.Substring(open + 1, close - open - 1).Split(',');
                if (parts.Length < 3) return false;
                double r, g, b, a = 1;
                if (!Json.TryDouble(parts[0].Trim(), out r) || !Json.TryDouble(parts[1].Trim(), out g) ||
                    !Json.TryDouble(parts[2].Trim(), out b)) return false;
                if (parts.Length > 3 && !Json.TryDouble(parts[3].Trim(), out a)) return false;
                color = new Color((float)(r / 255.0), (float)(g / 255.0), (float)(b / 255.0), (float)a);
                return true;
            }
            return ColorUtility.TryParseHtmlString(s, out color);
        }
    }

    // ====================================================================== reflection helpers
    static class Reflect
    {
        public static bool TrySet(object target, string property, object value)
        {
            PropertyInfo p = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            if (p == null || !p.CanWrite || p.GetSetMethod() == null)
                return false;
            object converted;
            if (!Convert(value, p.PropertyType, out converted))
                return false;
            p.SetValue(target, converted, null);
            return true;
        }

        public static void SetProperty(object target, string property, object value, string where, List<string> warnings)
        {
            PropertyInfo p = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
            if (p == null || !p.CanWrite || p.GetSetMethod() == null)
            {
                warnings.Add(where + ": " + target.GetType().Name + " has no public settable property '" + property + "'");
                return;
            }
            object converted;
            if (!Convert(value, p.PropertyType, out converted))
            {
                warnings.Add(where + ": cannot convert '" + Json.ToStr(value) + "' to " + p.PropertyType.Name + " for '" + property + "'");
                return;
            }
            p.SetValue(target, converted, null);
        }

        static bool Convert(object value, Type type, out object result)
        {
            result = null;
            double d;
            if (type == typeof(string)) { result = Json.ToStr(value); return true; }
            if (type == typeof(bool)) { result = Json.ToBool(value); return true; }
            if (type == typeof(float) && Json.TryDouble(value, out d)) { result = (float)d; return true; }
            if (type == typeof(double) && Json.TryDouble(value, out d)) { result = d; return true; }
            if (type == typeof(int) && Json.TryDouble(value, out d)) { result = (int)Math.Round(d); return true; }
            if (type == typeof(long) && Json.TryDouble(value, out d)) { result = (long)Math.Round(d); return true; }
            if (type.IsEnum)
            {
                string s = Json.ToStr(value).Replace("-", string.Empty).Replace("_", string.Empty);
                foreach (string n in Enum.GetNames(type))
                {
                    if (string.Equals(n, s, StringComparison.OrdinalIgnoreCase))
                    {
                        result = Enum.Parse(type, n);
                        return true;
                    }
                }
                return false;
            }
            if (typeof(IList<string>).IsAssignableFrom(type) || type == typeof(List<string>))
            {
                result = Json.ToStringList(value);
                return true;
            }
            return false;
        }
    }

    // ====================================================================== JSON
    static class Json
    {
        public static string ToStr(object v)
        {
            if (v == null) return string.Empty;
            string s = v as string;
            if (s != null) return s;
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double)
            {
                double d = (double)v;
                return Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15
                    ? ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture)
                    : d.ToString("R", CultureInfo.InvariantCulture);
            }
            var f = v as IFormattable;
            return f != null ? f.ToString(null, CultureInfo.InvariantCulture) : v.ToString();
        }

        public static bool ToBool(object v)
        {
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            string s = v as string;
            return s != null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" ||
                                 s.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        public static bool TryDouble(object v, out double d)
        {
            if (v is double) { d = (double)v; return true; }
            if (v is int) { d = (int)v; return true; }
            if (v is float) { d = (float)v; return true; }
            if (v is long) { d = (long)v; return true; }
            string s = v as string;
            if (s != null)
                return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            d = 0;
            return false;
        }

        public static List<string> ToStringList(object v)
        {
            var result = new List<string>();
            var list = v as List<object>;
            if (list != null)
            {
                foreach (object o in list)
                {
                    string s = ToStr(o).Trim();
                    if (s.Length > 0) result.Add(s);
                }
                return result;
            }
            foreach (string part in ToStr(v).Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(part);
            return result;
        }
    }

    /// <summary>Minimal JSON reader/writer (objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double).</summary>
    static class MiniJson
    {
        public static object Parse(string text)
        {
            var p = new Parser(text ?? string.Empty);
            object value = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd)
                throw p.Error("unexpected trailing characters");
            return value;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            string s = v as string;
            if (s != null) { WriteString(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float || v is int || v is long)
            {
                double d = System.Convert.ToDouble(v, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : Json.ToStr(d));
                return;
            }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key.ToString());
                    sb.Append(':');
                    Write(sb, kv.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string m_Text;
            int m_Pos;

            public Parser(string text)
            {
                m_Text = text;
                m_Pos = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            }

            public bool AtEnd { get { return m_Pos >= m_Text.Length; } }
            char Peek { get { return m_Pos < m_Text.Length ? m_Text[m_Pos] : '\0'; } }

            public FormatException Error(string message)
            {
                int line = 1, col = 1;
                for (int i = 0; i < m_Pos && i < m_Text.Length; i++)
                {
                    if (m_Text[i] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new FormatException("JSON " + message + " at line " + line + ", column " + col);
            }

            public void SkipWhitespace()
            {
                while (m_Pos < m_Text.Length && char.IsWhiteSpace(m_Text[m_Pos]))
                    m_Pos++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("unexpected end of input");
                char c = Peek;
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                }
                if (c == '-' || (c >= '0' && c <= '9'))
                    return ParseNumber();
                throw Error("unexpected character '" + c + "'");
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(m_Text, m_Pos, word, 0, word.Length) != 0)
                    throw Error("expected '" + word + "'");
                m_Pos += word.Length;
            }

            Dictionary<string, object> ParseObject()
            {
                m_Pos++; // {
                var d = new Dictionary<string, object>();
                SkipWhitespace();
                if (Peek == '}') { m_Pos++; return d; }
                while (true)
                {
                    SkipWhitespace();
                    if (Peek != '"') throw Error("expected a property name");
                    string key = ParseString();
                    SkipWhitespace();
                    if (Peek != ':') throw Error("expected ':'");
                    m_Pos++;
                    d[key] = ParseValue();
                    SkipWhitespace();
                    char c = Peek;
                    m_Pos++;
                    if (c == ',') continue;
                    if (c == '}') return d;
                    m_Pos--;
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> ParseArray()
            {
                m_Pos++; // [
                var list = new List<object>();
                SkipWhitespace();
                if (Peek == ']') { m_Pos++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    char c = Peek;
                    m_Pos++;
                    if (c == ',') continue;
                    if (c == ']') return list;
                    m_Pos--;
                    throw Error("expected ',' or ']'");
                }
            }

            string ParseString()
            {
                m_Pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = m_Text[m_Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("unterminated escape");
                    char e = m_Text[m_Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (m_Pos + 4 > m_Text.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(m_Text.Substring(m_Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            m_Pos += 4;
                            break;
                        default: throw Error("bad escape '\\" + e + "'");
                    }
                }
            }

            double ParseNumber()
            {
                int start = m_Pos;
                while (m_Pos < m_Text.Length && "+-0123456789.eE".IndexOf(m_Text[m_Pos]) >= 0)
                    m_Pos++;
                double d;
                if (!double.TryParse(m_Text.Substring(start, m_Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Error("bad number");
                return d;
            }
        }
    }
}
