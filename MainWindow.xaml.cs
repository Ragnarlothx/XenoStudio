using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace XenoStudio
{
    public class Msg
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Who { get; set; }          // xeno | claude | codex | system | shell
        public string Text { get; set; } = "";
        public List<string> Tools { get; set; } = new();
        public List<string> Images { get; set; } = new();   // attached pictures, relative to data\uploads ("<chatId>/<file>")
        public List<FileEdit> Files { get; set; } = new();  // files an AI changed during this reply (change cards → diff view)
        public long Ts { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        public bool Error { get; set; }
    }

    public class AgentState
    {
        public string SessionId { get; set; }
        public int Cursor { get; set; }           // index of the first transcript message this agent hasn't been shown
    }

    public class Chat
    {
        public string Id { get; set; } = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        public string Title { get; set; } = "New chat";
        public long Updated { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        public List<Msg> Messages { get; set; } = new();
        public Dictionary<string, AgentState> Agents { get; set; } = new() { ["claude"] = new(), ["codex"] = new(), ["deepseek"] = new(), ["grok"] = new() };
    }

    public partial class MainWindow : Window
    {
        static readonly string AppData = Paths.Data;
        static readonly string ChatDir = Paths.Chats;
        static readonly string SettingsFile = Paths.SettingsFile;
        static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        static readonly Regex Mention = new(@"(?<![\w@])@(claude|codex|deepseek|grok|both|all|everyone)\b", RegexOptions.IgnoreCase);

        Settings S = new();
        Chat chat = new();
        readonly Dictionary<string, Agent> agents = new() { ["claude"] = new ClaudeAgent(), ["codex"] = new CodexAgent(), ["deepseek"] = new ApiAgent("deepseek", "DeepSeek"), ["grok"] = new ApiAgent("grok", "Grok") };
        readonly List<string> queue = new();
        bool busy;
        int aiTurns, alternate;
        string running;
        EditTracker tracker;   // the running AI turn's file snapshots (null between turns)
        bool startupSent;
        List<string> mcpRefreshed = new();
        CancellationTokenSource cts = new();

        public MainWindow()
        {
            InitializeComponent();
            Title = "XenoStudio " + AppVersion;
            RestoreBounds_();
            Loaded += async (_, _) => await Init();
            Closing += (_, _) => { try { cts.Cancel(); } catch { } SaveChat(); SaveBounds(); };
        }

        // ---------------------------------------------------------------- single instance
        bool webReady;
        string pendingSend;

        /// <summary>The exe was launched again: come to the front, and run its --send like a first launch would.</summary>
        public void OnSecondLaunch(string[] args)
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show(); Activate();
            Topmost = true; Topmost = false;   // reliably jumps above other windows
            int si = Array.IndexOf(args, "--send");
            if (si >= 0 && si + 1 < args.Length) StartWithMessage(args[si + 1]);
        }

        void StartWithMessage(string text)
        {
            if (!webReady) { pendingSend = text; return; }
            if (busy) Stop();
            SaveChat(); chat = new Chat();
            Post(new { type = "chat", chat, chats = ListChats() });
            UserSend(text, "auto");
        }

        // ---------------------------------------------------------------- window position (data\window.json)
        static readonly string BoundsFile = Path.Combine(Paths.Data, "window.json");
        record Bounds(double Left, double Top, double Width, double Height, bool Maximized);

        void RestoreBounds_()
        {
            try
            {
                if (!File.Exists(BoundsFile)) return;
                var b = JsonSerializer.Deserialize<Bounds>(File.ReadAllText(BoundsFile));
                if (b == null || b.Width < MinWidth || b.Height < MinHeight) return;
                // only reuse it if the title bar would land on a screen that still exists
                var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (!vs.IntersectsWith(new Rect(b.Left, b.Top, b.Width, 40))) return;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
                if (b.Maximized) WindowState = WindowState.Maximized;
            }
            catch { }
        }

        void SaveBounds()
        {
            try
            {
                var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                if (r.IsEmpty) return;
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(BoundsFile, JsonSerializer.Serialize(new Bounds(r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized)));
            }
            catch { }
        }

        async Task Init()
        {
            Directory.CreateDirectory(ChatDir);
            Directory.CreateDirectory(Paths.Profiles);
            // first run: start from the settings XenoDuo was using (read only — XenoDuo's own files are never touched)
            var src = File.Exists(SettingsFile) ? SettingsFile : File.Exists(Paths.OldXenoDuoSettings) ? Paths.OldXenoDuoSettings : null;
            try { if (src != null) S = JsonSerializer.Deserialize<Settings>(File.ReadAllText(src), J) ?? new(); } catch { S = new(); }
            S.Clamp();
            try { mcpRefreshed = S.RefreshStaleMcp(); } catch (Exception ex) { CrashLog.Write("mcp refresh", ex); }
            if (src != SettingsFile || mcpRefreshed.Count > 0) SaveSettings();
            ThemeChrome.Apply(this, web, S.Theme);   // title bar + background before the page paints, so there's no grey flash
            var last = ListChats().FirstOrDefault();
            if (last != null) chat = LoadChat(last.Id) ?? new Chat();

            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppData, "WebView2"));
            await web.EnsureCoreWebView2Async(env);
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping("xenostudio.app", Path.Combine(AppContext.BaseDirectory, "web"), CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(Paths.Uploads);
            core.SetVirtualHostNameToFolderMapping("uploads.xenostudio.app", Paths.Uploads, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += (_, e) => { try { OnMessage(JsonNode.Parse(e.WebMessageAsJson)); } catch (Exception ex) { Notice("Error: " + ex.Message); } };
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenUrl(e.Uri); };
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://xenostudio.app/")) { e.Cancel = true; OpenUrl(e.Uri); } };
            web.Source = new Uri("https://xenostudio.app/index.html");
        }

        // ---------------------------------------------------------------- bridge
        void Post(object o)
        {
            var json = JsonSerializer.Serialize(o, J);
            Dispatcher.BeginInvoke(() => { try { web.CoreWebView2?.PostWebMessageAsJson(json); } catch { } });
        }
        void Notice(string text) => Post(new { type = "notice", text });
        void PostStatus()
        {
            var st = agents.Keys.ToDictionary(k => k, k => running == k ? "running" : queue.Contains(k) ? "queued" : !Enabled(k) ? "off" : "idle");
            Post(new { type = "status", agents = st, busy });
        }

        void OnMessage(JsonNode n)
        {
            switch ((string)n["cmd"])
            {
                case "ready":
                    Post(new { type = "init", settings = S, chat, chats = ListChats(), exe = ExeInfo(), dataDir = Paths.Data, version = AppVersion,
                                promptDefaults = new { template = DefaultPromptTemplate, footer = DefaultPromptFooter, placeholders = PromptPlaceholders } });
                    PostStatus();
                    _ = PostRuntime();
                    _ = SetupCheckOnStart();
                    webReady = true;
                    if (mcpRefreshed.Count > 0) Notice("Updated MCP servers whose paths had gone stale: " + string.Join(", ", mcpRefreshed));
                    // XenoStudio.exe --send "message"  → starts a new chat with that message (scripting / shortcuts)
                    var argv = Environment.GetCommandLineArgs();
                    int si = Array.IndexOf(argv, "--send");
                    if (!startupSent && si > 0 && si + 1 < argv.Length) { startupSent = true; StartWithMessage(argv[si + 1]); }
                    if (pendingSend != null) { var p = pendingSend; pendingSend = null; StartWithMessage(p); }
                    break;
                case "send": UserSend((string)n["text"] ?? "", (string)n["target"] ?? "auto", SaveImages(n["images"] as JsonArray)); break;
                // ---- code preview panel (web\ shows it with Monaco from web\lib\vs)
                case "previewFile":
                    Reply(n, "previewFile", () => CodePreview.ReadFile((string)n["path"] ?? "", S));
                    break;
                case "saveCode":
                    {
                        var name = CodePreview.SafeName((string)n["name"]);
                        var saveDlg = new Microsoft.Win32.SaveFileDialog { Title = "Save code", FileName = name, InitialDirectory = S.PrimaryWorkDir, Filter = "All files|*.*" };
                        if (saveDlg.ShowDialog(this) == true)
                            Reply(n, "savedCode", () => { File.WriteAllText(saveDlg.FileName, (string)n["text"] ?? "", new UTF8Encoding(false)); return new { path = saveDlg.FileName }; });
                        else Post(new { type = "savedCode", rid = (string)n["rid"], ok = false, cancelled = true });
                    }
                    break;
                case "openInXenoEdit":
                    Reply(n, "xenoEdit", () =>
                    {
                        var path = (string)n["path"];
                        if (string.IsNullOrWhiteSpace(path)) path = CodePreview.WriteTemp((string)n["text"] ?? "", (string)n["name"]);
                        return CodePreview.OpenInXenoEdit(path, S);
                    });
                    break;
                case "xenoEditInfo":
                    Post(new { type = "xenoEditInfo", exe = CodePreview.FindXenoEdit(S) });
                    break;
                // ---- shared editor: AI change diffs, saving your edits, live reload
                case "fileDiff":
                    Reply(n, "fileDiff", () => EditTracker.Diff((string)n["snap"] ?? ""));
                    break;
                case "saveFile":
                    Reply(n, "savedFile", () => SaveUserEdit((string)n["path"] ?? "", (string)n["text"] ?? ""));
                    break;
                // ---- first-run setup (Setup.cs): checklist, installs, sign-ins, MCP catalog
                case "setupScan": _ = ReplyAsync(n, "setupScan", async () => new { items = await Setup.Scan(S, Exe("claude"), Exe("codex")), setupDone = S.SetupDone, winget = Setup.HasWinget }); break;
                case "setupInstall": _ = SetupInstall(n); break;
                case "setupLogin": _ = SetupLogin(n); break;
                case "setupApiKey": _ = SetupApiKey(n); break;
                case "setupSkipLogin":   // { id: "claude"|"codex", skip: bool }
                    {
                        var sid = (string)n["id"]; var skip = (bool?)n["skip"] ?? true;
                        if (sid is "claude" or "codex")
                        {
                            S.SkipLogin ??= new();
                            S.SkipLogin.Remove(sid);
                            if (skip) S.SkipLogin.Add(sid);
                            SaveSettings();
                        }
                        _ = ReplyAsync(n, "setupSkipLogin", async () => new { item = await ScanOne(sid + "-login") });
                    }
                    break;
                case "setupDone": S.SetupDone = true; SaveSettings(); Post(new { type = "settings", settings = S, exe = ExeInfo() }); break;
                case "mcpCatalog": Reply(n, "mcpCatalog", () => new { servers = Setup.Catalog(S), node = Setup.Which("npx") != null, uv = Setup.Which("uvx") != null }); break;
                case "mcpInstall": _ = McpInstall(n); break;
                case "watchFile": Watch((string)n["path"], true); break;
                case "unwatchFile": Watch((string)n["path"], false); break;
                case "openImage":
                    {
                        var p = UploadPath((string)n["path"]);
                        if (p != null && File.Exists(p)) try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch { }
                    }
                    break;
                case "stop": Stop(); break;
                case "newChat": if (busy) Stop(); SaveChat(); chat = new Chat(); Post(new { type = "chat", chat, chats = ListChats() }); break;
                case "openChat":
                    if (busy) Stop(); SaveChat();
                    chat = LoadChat((string)n["id"]) ?? new Chat();
                    Post(new { type = "chat", chat, chats = ListChats() });
                    break;
                case "deleteChat":
                    var id = (string)n["id"];
                    // edit snapshots are stored per message: read the chat before its file goes
                    try { var gone = LoadChat(id); if (gone != null) EditTracker.DeleteForMessages(gone.Messages.Select(m => m.Id)); } catch { }
                    try { File.Delete(Path.Combine(ChatDir, id + ".json")); } catch { }
                    try { var up = UploadPath(id); if (up != null && Directory.Exists(up)) Directory.Delete(up, true); } catch { }
                    if (chat.Id == id) { if (busy) Stop(); chat = new Chat(); Post(new { type = "chat", chat, chats = ListChats() }); }
                    else Post(new { type = "chats", chats = ListChats() });
                    break;
                case "saveSettings":
                    S = n["settings"].Deserialize<Settings>(J) ?? S;
                    S.Clamp();
                    SaveSettings();
                    ThemeChrome.Apply(this, web, S.Theme);
                    Post(new { type = "settings", settings = S, exe = ExeInfo() });
                    _ = PostRuntime();
                    break;
                case "previewPrompt":
                    {
                        // shows exactly what an AI receives: system prompt + the footer of its next turn
                        var pid = (string)n["id"] ?? "claude";
                        if (!Seats.Contains(pid)) pid = "claude";
                        var saved = S;
                        if (n["settings"] != null) { S = n["settings"].Deserialize<Settings>(J) ?? S; S.Clamp(); }   // preview unsaved edits
                        try { Post(new { type = "promptPreview", id = pid, system = SystemNote(pid), footer = Footer(pid) }); }
                        finally { S = saved; }
                    }
                    break;
                case "resetSettings":
                    S = new Settings(); S.Clamp(); SaveSettings();
                    ThemeChrome.Apply(this, web, S.Theme);
                    Post(new { type = "settings", settings = S, exe = ExeInfo() });
                    break;
                case "importMcp":
                    // returns candidates only; the UI merges the ones the user keeps and saves
                    Post(new { type = "mcpImport", servers = Util.ImportMcp() });
                    break;
                case "updateRuntime": _ = UpdateRuntime((string)n["which"] ?? "both"); break;
                case "runtimeInfo": _ = PostRuntime(); break;
                case "openData": try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.Data}\"") { UseShellExecute = true }); } catch { } break;
                case "openProfile":
                    // creates an empty Claude settings profile on first open; passed to claude.exe with --settings
                    try
                    {
                        if (!File.Exists(Paths.ClaudeSettingsProfile)) File.WriteAllText(Paths.ClaudeSettingsProfile, "{\n}\n");
                        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Paths.ClaudeSettingsProfile}\"") { UseShellExecute = true });
                    }
                    catch { }
                    break;
                case "exportSettings": ExportSettings(); break;
                case "pickFile":
                    var fd = new Microsoft.Win32.OpenFileDialog { Title = (string)n["title"] ?? "Choose file", Filter = (string)n["filter"] ?? "All files|*.*" };
                    if (fd.ShowDialog(this) == true) Post(new { type = "pickedFile", key = (string)n["key"], path = fd.FileName });
                    break;
                case "workspaceDrives":
                    Post(new { type = "workspaceDrives", drives = DriveInfo.GetDrives().Select(d => new { path = d.Name, kind = d.DriveType.ToString(), ready = DriveReady(d) }).ToArray() });
                    break;
                case "pickDir":
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Shared working folder", InitialDirectory = S.PrimaryWorkDir };
                    if (dlg.ShowDialog(this) == true) Post(new { type = "pickedDir", path = dlg.FolderName });
                    break;
                case "openDir": try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{S.PrimaryWorkDir}\"") { UseShellExecute = true }); } catch { } break;
                case "openUrl": OpenUrl((string)n["url"]); break;
                case "export": ExportMarkdown(); break;
            }
        }

        // ---------------------------------------------------------------- shared editor
        /// <summary>
        /// Saves an edit Xeno made in the editor panel, then notes it in the chat so the AIs see it on their next turn
        /// ("[Xeno]: edited src\cart.js in the editor (+3 −1)").
        /// </summary>
        object SaveUserEdit(string path, string text)
        {
            if (!Path.IsPathRooted(path)) throw new InvalidOperationException("Only files opened from disk can be saved; use Save as… for code blocks.");
            var old = File.Exists(path) ? File.ReadAllText(path) : "";
            File.WriteAllText(path, text, new UTF8Encoding(false));
            var la = old.Replace("\r\n", "\n").Split('\n'); var lb = text.Replace("\r\n", "\n").Split('\n');
            int added = lb.Except(la).Count(), removed = la.Except(lb).Count();
            var shown = ShortPath(path);
            AddMsg(new Msg { Who = "xeno", Text = $"_edited `{shown}` in the editor (+{added} −{removed})_" });
            SaveChat();
            return new { path, added, removed };
        }

        string ShortPath(string full)
        {
            foreach (var dir in new[] { S.PrimaryWorkDir }.Concat(S.ExtraDirs(includeClaudeExtras: true)))
                try { var rel = Path.GetRelativePath(dir, full); if (!rel.StartsWith("..") && !Path.IsPathRooted(rel)) return rel; } catch { }
            return full;
        }

        static readonly Regex AbsPath = new(@"[A-Za-z]:\\[^\s""'<>|*?`]+", RegexOptions.Compiled);
        static readonly Regex RelPath = new(@"(?<![\w:\\/])(?:[\w.-]+[\\/])*[\w.-]+\.[A-Za-z0-9]{1,8}(?![\w\\/])", RegexOptions.Compiled);

        /// <summary>File paths a prompt mentions that exist on disk (absolute, or relative to the work folders).</summary>
        IEnumerable<string> MentionedPaths(string text)
        {
            var found = new List<string>();
            foreach (Match m in AbsPath.Matches(text ?? ""))
            {
                var p = m.Value.TrimEnd('.', ',', ')', ':', ';');
                if (File.Exists(p)) found.Add(p);
            }
            var dirs = new[] { S.PrimaryWorkDir }.Concat(S.ExtraDirs(includeClaudeExtras: true)).ToList();
            foreach (Match m in RelPath.Matches(text ?? ""))
                foreach (var d in dirs)
                    try { var p = Path.Combine(d, m.Value); if (File.Exists(p)) { found.Add(p); break; } } catch { }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(60);
        }

        string ResolveEditPath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return null;
            try { return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(S.PrimaryWorkDir, p)); } catch { return null; }
        }

        readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Live reload: while a file is open in the panel, changes on disk (an AI editing it) are pushed as fileChanged.</summary>
        void Watch(string path, bool on)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return;
            if (!on) { if (watchers.Remove(path, out var w)) w.Dispose(); return; }
            if (watchers.ContainsKey(path) || !File.Exists(path) || watchers.Count >= 32) return;
            var fsw = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
            { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
            DateTime last = DateTime.MinValue;
            void Changed(object _, FileSystemEventArgs e)
            {
                if ((DateTime.UtcNow - last).TotalMilliseconds < 400) return;   // editors fire several events per save
                last = DateTime.UtcNow;
                Post(new { type = "fileChanged", path, deleted = !File.Exists(path) });
            }
            fsw.Changed += Changed; fsw.Created += Changed; fsw.Deleted += Changed; fsw.Renamed += (s, e) => Changed(s, e);
            watchers[path] = fsw;
        }

        /// <summary>Runs a request and posts { type, rid, ok, data } or { type, rid, ok:false, error } back to the page.</summary>
        void Reply(JsonNode n, string type, Func<object> work)
        {
            var rid = (string)n["rid"];
            try { Post(new { type, rid, ok = true, data = work() }); }
            catch (Exception ex) { Post(new { type, rid, ok = false, error = ex.Message }); }
        }

        async Task ReplyAsync(JsonNode n, string type, Func<Task<object>> work)
        {
            var rid = (string)n["rid"];
            try { Post(new { type, rid, ok = true, data = await work() }); }
            catch (Exception ex) { Post(new { type, rid, ok = false, error = ex.Message }); }
        }

        // ---------------------------------------------------------------- setup
        string Exe(string id) => agents[id].ResolveExe(S);
        Task<SetupItem> ScanOne(string id) => Setup.ScanOne(id, S, Exe("claude"), Exe("codex"));
        bool setupBusy;

        /// <summary>On start: if a CLI or sign-in the enabled AIs need is missing, ask the page to open the setup wizard.</summary>
        async Task SetupCheckOnStart()
        {
            try
            {
                var items = await Setup.Scan(S, Exe("claude"), Exe("codex"));
                var missing = items.Where(i => i.Required && !i.Ok).ToList();
                // the wizard pops up until setup has been finished once; after that a missing item is a notice, not a modal every launch
                if (missing.Count > 0 && !S.SetupDone) Post(new { type = "setupNeeded", items, firstRun = true });
                else if (missing.Count > 0) Notice("Setup: " + string.Join(", ", missing.Select(i => i.Name)) + " needs attention. Open Settings → Setup tools and sign-in.");
                else if (!S.SetupDone) await Dispatcher.InvokeAsync(() => { S.SetupDone = true; SaveSettings(); });
            }
            catch (Exception ex) { CrashLog.Write("setup-scan", ex); }
        }

        /// <summary>{ id } or { ids:[…] }. One native confirm lists everything (including prerequisites) before anything runs.</summary>
        async Task SetupInstall(JsonNode n)
        {
            var rid = (string)n["rid"];
            var ids = (n["ids"] as JsonArray)?.Select(x => (string)x).ToList() ?? new List<string> { (string)n["id"] };
            if (setupBusy) { Post(new { type = "setupInstall", rid, ok = false, error = "Another install is still running." }); return; }
            // prerequisites first, each once, in a sensible order
            var order = new[] { "node", "git", "uv", "claude", "codex" };
            var steps = ids.SelectMany(i => Setup.Plan(i).needsFirst.Append(i)).Where(i => Setup.Plan(i).what != null)
                           .Distinct().OrderBy(i => Array.IndexOf(order, i)).ToList();
            if (steps.Count == 0) { Post(new { type = "setupInstall", rid, ok = false, error = "Nothing to install." }); return; }

            var text = "XenoStudio will install:\n\n" + string.Join("\n", steps.Select(i => "•  " + Setup.Plan(i).what)) +
                       "\n\nThe downloads come from the official sources. Windows may ask for permission (UAC).\n\nContinue?";
            if (MessageBox.Show(this, text, "XenoStudio setup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            { Post(new { type = "setupInstall", rid, ok = false, cancelled = true }); return; }

            setupBusy = true;
            try
            {
                var skip = new HashSet<string>();
                foreach (var id in steps)
                {
                    if (skip.Contains(id)) continue;
                    Post(new { type = "setupLog", id, line = $"Installing {id}…" });
                    var ok = await Setup.Install(id, line => Post(new { type = "setupLog", id, line }));
                    if (!ok && id == "node" && steps.Contains("codex"))
                    {   // codex needs npm; check whether node is usable anyway (winget says non-zero when it's already there)
                        Setup.RefreshPath();
                        if (Setup.Which("npm") == null) { Post(new { type = "setupLog", id = "codex", line = "Skipping Codex: Node.js isn't available yet." }); skip.Add("codex"); }
                    }
                }
                var rescan = steps.Concat(steps.Where(i => i is "claude" or "codex").Select(i => i + "-login")).Distinct();
                var items = new List<SetupItem>();
                foreach (var id in rescan) items.Add(await ScanOne(id));
                Post(new { type = "setupInstall", rid, ok = true, data = new { items } });
                Post(new { type = "settings", settings = S, exe = ExeInfo() });
                _ = PostRuntime();
            }
            catch (Exception ex) { Post(new { type = "setupInstall", rid, ok = false, error = ex.Message }); }
            finally { setupBusy = false; }
        }

        /// <summary>{ id: "claude" | "codex" } — opens the CLI's own browser sign-in in a console window; replies when it closes.</summary>
        async Task SetupLogin(JsonNode n)
        {
            var id = (string)n["id"];
            await ReplyAsync(n, "setupLogin", async () =>
            {
                if (id is not ("claude" or "codex")) throw new Exception("unknown sign-in " + id);
                var exe = Exe(id);
                if (!Path.IsPathRooted(exe) || !File.Exists(exe)) throw new Exception($"Install the {(id == "claude" ? "Claude Code" : "Codex")} CLI first.");
                await Setup.Login(id, exe);
                return new { item = await ScanOne(id + "-login") };
            });
        }

        /// <summary>{ id: "claude" | "codex", key } — API key instead of an account sign-in. The key is never logged or echoed back.</summary>
        async Task SetupApiKey(JsonNode n)
        {
            var id = (string)n["id"]; var key = ((string)n["key"] ?? "").Trim();
            await ReplyAsync(n, "setupApiKey", async () =>
            {
                if (key.Length < 20 || key.Any(char.IsWhiteSpace)) throw new Exception("That doesn't look like an API key.");
                if (id == "claude")
                {
                    S.ClaudeEnv = Setup.SetEnvLine(S.ClaudeEnv, "ANTHROPIC_API_KEY", key);
                    SaveSettings();
                    Post(new { type = "settings", settings = S, exe = ExeInfo() });
                }
                else if (id == "codex")
                {
                    var exe = Exe("codex");
                    if (!Path.IsPathRooted(exe) || !File.Exists(exe)) throw new Exception("Install the Codex CLI first.");
                    await Setup.SaveCodexKey(exe, key);
                }
                else throw new Exception("unknown sign-in " + id);
                return new { item = await ScanOne(id + "-login") };
            });
        }

        /// <summary>{ id } from the catalog — test-starts the server (downloads it the first time), then adds or re-enables it.</summary>
        async Task McpInstall(JsonNode n)
        {
            var id = (string)n["id"];
            await ReplyAsync(n, "mcpInstall", async () =>
            {
                Setup.RefreshPath();
                var entry = Setup.Catalog(S).FirstOrDefault(e => e.Id == id) ?? throw new Exception("unknown MCP server " + id);
                if (entry.Needs == "node" && Setup.Which("npx") == null) throw new Exception("Needs Node.js. Install it in the setup checklist first.");
                if (entry.Needs == "uv" && Setup.Which("uvx") == null) throw new Exception("Needs uv. Install it in the setup checklist first.");
                var m = entry.Make();
                var tools = await Setup.TestMcp(m, line => Post(new { type = "setupLog", id = "mcp:" + id, line }));
                S.McpServers ??= new();
                var old = S.McpServers.FirstOrDefault(x => x.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
                if (old != null) { m.Claude = old.Claude; m.Codex = old.Codex; S.McpServers[S.McpServers.IndexOf(old)] = m; }
                else S.McpServers.Add(m);
                SaveSettings();
                Post(new { type = "settings", settings = S, exe = ExeInfo() });
                return new { tools, servers = Setup.Catalog(S) };
            });
        }

        static bool DriveReady(DriveInfo drive) { try { return drive.IsReady; } catch { return false; } }

        static void OpenUrl(string url)
        {
            if (url != null && (url.StartsWith("https://") || url.StartsWith("http://")))
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        // ---------------------------------------------------------------- settings / runtime helpers
        static readonly string AppVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Paths.Data);
                var tmp = SettingsFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(S, J));
                File.Move(tmp, SettingsFile, true);
            }
            catch { }
        }

        object ExeInfo() => new { claude = agents["claude"].ResolveExe(S), codex = agents["codex"].ResolveExe(S), deepseek = agents["deepseek"].ResolveExe(S), grok = agents["grok"].ResolveExe(S) };

        static async Task<string> ExeVersion(string exe)
        {
            try
            {
                if (!File.Exists(exe)) return null;
                var psi = new ProcessStartInfo(exe, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.Environment["DISABLE_AUTOUPDATER"] = "1";
                using var p = Process.Start(psi);
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (await Task.WhenAny(outTask, Task.Delay(15000)) != outTask) { try { p.Kill(true); } catch { } return null; }
                return (await outTask).Trim();
            }
            catch { return null; }
        }

        async Task PostRuntime()
        {
            async Task<object> One(string bundled, string installed, string inUse) => new
            {
                bundled = File.Exists(bundled) ? bundled : null,
                bundledVersion = await ExeVersion(bundled),
                installed,
                installedVersion = installed != null ? await ExeVersion(installed) : null,
                inUse,
            };
            var claude = await One(Paths.BundledClaude, ClaudeAgent.FindInstalled(S), agents["claude"].ResolveExe(S));
            var codex = await One(Paths.BundledCodex, CodexAgent.FindInstalled(), agents["codex"].ResolveExe(S));
            Post(new { type = "runtime", claude, codex });
        }

        // Copies the installed CLI over the pinned one in runtime\. Only ever runs when the user asks.
        async Task UpdateRuntime(string which)
        {
            if (busy) { Notice("Stop the running turn first."); return; }
            var jobs = new List<(string name, string src, string dst)>();
            if (which is "claude" or "both") jobs.Add(("Claude", ClaudeAgent.FindInstalled(S), Paths.BundledClaude));
            if (which is "codex" or "both") jobs.Add(("Codex", CodexAgent.FindInstalled(), Paths.BundledCodex));
            foreach (var (name, src, dst) in jobs)
            {
                if (src == null) { Notice($"{name}: no installed copy found to update from."); continue; }
                try
                {
                    Directory.CreateDirectory(Paths.Runtime);
                    await Task.Run(() =>
                    {
                        File.Copy(src, dst + ".new", true); File.Move(dst + ".new", dst, true);
                        // codex.exe shells out to helpers that live next to it (command runner, sandbox setup, code-mode host)
                        if (name == "Codex")
                            foreach (var h in Directory.GetFiles(Path.GetDirectoryName(src), "codex-*.exe"))
                                File.Copy(h, Path.Combine(Paths.Runtime, Path.GetFileName(h)), true);
                    });
                    Notice($"{name}: bundled copy updated from {src}");
                }
                catch (Exception ex) { Notice($"{name}: update failed ({ex.Message})"); }
            }
            await PostRuntime();
        }

        void ExportSettings()
        {
            try
            {
                var path = Path.Combine(S.PrimaryWorkDir, "XenoStudio-settings.json");
                var copy = JsonSerializer.SerializeToNode(S, J);
                copy["deepSeekApiKey"] = ""; copy["grokApiKey"] = "";   // never export API keys
                foreach (var envKey in new[] { "claudeEnv", "codexEnv" })    // nor keys/tokens set as environment lines (setup's API-key option)
                    copy[envKey] = string.Join("\n", ((string)copy[envKey] ?? "").Replace("\r", "").Split('\n')
                        .Where(l => !Regex.IsMatch(l.Split('=')[0], "KEY|TOKEN|SECRET|PASSWORD", RegexOptions.IgnoreCase)));
                File.WriteAllText(path, copy.ToJsonString(J), new UTF8Encoding(false));
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                Notice("Settings exported to " + path + " (check MCP env/headers for secrets before sharing)");
            }
            catch (Exception ex) { Notice("Export failed: " + ex.Message); }
        }

        // ---------------------------------------------------------------- shell ("!command" in the composer)
        async Task RunShell(string command)
        {
            var msg = new Msg { Who = "shell", Tools = new() { "$ " + command } };
            chat.Messages.Add(msg);
            Post(new { type = "start", msg });
            busy = true; PostStatus();
            var token = cts.Token;
            var outp = new StringBuilder();
            int code = -1;
            try
            {
                var (exe, pre) = S.Shell switch
                {
                    "cmd" => ("cmd.exe", new[] { "/d", "/c" }),
                    "pwsh" => ("pwsh.exe", new[] { "-NoProfile", "-NonInteractive", "-Command" }),
                    _ => ("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command" }),
                };
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = S.PrimaryWorkDir,
                };
                foreach (var a in pre) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add(S.Shell == "cmd" ? command : "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + command);
                using var p = new Process { StartInfo = psi };
                void Line(string l) { if (l == null) return; lock (outp) { if (outp.Length < 200_000) outp.AppendLine(l); } Post(new { type = "delta", id = msg.Id, text = l + "\n" }); }
                p.OutputDataReceived += (_, e) => Line(e.Data);
                p.ErrorDataReceived += (_, e) => Line(e.Data);
                p.Start(); p.StandardInput.Close();
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                using var reg = token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
                await p.WaitForExitAsync();
                code = p.ExitCode;
            }
            catch (Exception ex) { lock (outp) outp.AppendLine("Error: " + ex.Message); msg.Error = true; }
            string body; lock (outp) body = outp.ToString().TrimEnd();
            if (body.Length > 20000) body = body.Substring(0, 20000) + "\n… (truncated)";
            msg.Text = "```\n" + (body.Length == 0 ? "(no output)" : body.Replace("```", "`​``")) + "\n```\n" +
                       (token.IsCancellationRequested ? "_stopped_" : $"_exit code {code}_");
            busy = false;
            Post(new { type = "done", msg });
            SaveChat(); PostStatus();
            if (queue.Count > 0) _ = Pump();
        }

        // ---------------------------------------------------------------- turn-taking
        static readonly string[] Seats = { "claude", "codex", "deepseek", "grok" };
        static readonly string[] Backups = { "deepseek", "grok" };   // chat-only API seats: always last, optional fallback
        static bool IsBackup(string id) => Backups.Contains(id);
        string Display(string who) => who switch { "xeno" => S.UserName, "claude" => "Claude", "codex" => "Codex", "deepseek" => "DeepSeek", "grok" => "Grok", "shell" => "Shell", _ => "System" };
        bool Enabled(string id) => id switch { "claude" => S.ClaudeEnabled, "codex" => S.CodexEnabled, "deepseek" => S.DeepSeekEnabled, "grok" => S.GrokEnabled, _ => false };
        IEnumerable<string> On() => Seats.Where(Enabled);

        void AddMsg(Msg m)
        {
            chat.Messages.Add(m);
            if (m.Who == "xeno" && chat.Title == "New chat")
                chat.Title = m.Text.Length == 0 ? "Picture" : m.Text.Length > 60 ? m.Text.Substring(0, 60).Trim() + "…" : m.Text.Trim();
            Post(new { type = "msg", msg = m });
        }

        void Enqueue(string id) { if (Enabled(id) && !queue.Contains(id) && running != id) queue.Add(id); }

        // ---------------------------------------------------------------- pictures
        static readonly Dictionary<string, string> ImageExt = new()
        { ["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/gif"] = ".gif", ["image/webp"] = ".webp", ["image/bmp"] = ".bmp" };
        const int MaxImageBytes = 20 * 1024 * 1024;

        /// <summary>Saves pictures sent from the composer ({ name, data: "data:image/png;base64,…" }) under data\uploads\&lt;chatId&gt;.</summary>
        List<string> SaveImages(JsonArray list)
        {
            var saved = new List<string>();
            if (list == null) return saved;
            foreach (var item in list)
            {
                try
                {
                    var data = (string)item?["data"] ?? "";
                    var m = Regex.Match(data, @"^data:(image/[\w.+-]+);base64,(.+)$", RegexOptions.Singleline);
                    if (!m.Success || !ImageExt.TryGetValue(m.Groups[1].Value.ToLowerInvariant(), out var ext)) { Notice("Skipped a file that isn't a PNG, JPG, GIF, WebP or BMP picture."); continue; }
                    var bytes = Convert.FromBase64String(m.Groups[2].Value);
                    if (bytes.Length > MaxImageBytes) { Notice("Skipped a picture over 20 MB."); continue; }
                    var stem = Regex.Replace(Path.GetFileNameWithoutExtension((string)item["name"] ?? "picture"), @"[^\w\-]+", "_").Trim('_');
                    if (stem.Length == 0 || stem.Length > 40) stem = "picture";
                    var rel = $"{chat.Id}/{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..6]}-{stem}{ext}";
                    var full = UploadPath(rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllBytes(full, bytes);
                    saved.Add(rel);
                }
                catch (Exception ex) { Notice("Couldn't save a picture: " + ex.Message); }
            }
            return saved;
        }

        /// <summary>data\uploads-relative path → absolute, refusing anything that would escape the uploads folder.</summary>
        static string UploadPath(string rel)
        {
            if (string.IsNullOrWhiteSpace(rel)) return null;
            var full = Path.GetFullPath(Path.Combine(Paths.Uploads, rel.Replace('/', Path.DirectorySeparatorChar)));
            return full.StartsWith(Path.GetFullPath(Paths.Uploads) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        void UserSend(string text, string target, List<string> images = null)
        {
            text = text.Trim();
            images ??= new();
            if (text.Length == 0 && images.Count == 0) return;
            if (images.Count == 0 && text.StartsWith("!") && text.Length > 1)
            {
                if (!S.ShellEnabled) { Notice("Shell commands are off (Settings → Commands)."); return; }
                if (busy) { Notice("Wait for the current turn to finish (or Stop) before running a command."); return; }
                _ = RunShell(text.Substring(1).Trim());
                return;
            }
            AddMsg(new Msg { Who = "xeno", Text = text, Images = images });

            var targets = new List<string>();
            if (Seats.Contains(target)) targets.Add(target);
            else if (target == "both") targets.AddRange(Order().Where(x => !IsBackup(x)));   // the two main AIs
            else if (target == "all") targets.AddRange(Order());
            else
            {
                foreach (Match m in Mention.Matches(text))
                {
                    var w = m.Groups[1].Value.ToLowerInvariant();
                    if (Seats.Contains(w)) { if (!targets.Contains(w)) targets.Add(w); }
                    else foreach (var o in Order().Where(x => w != "both" || !IsBackup(x))) if (!targets.Contains(o)) targets.Add(o);
                }
                if (targets.Count == 0) targets.AddRange(Order(auto: true));
            }
            if (!targets.Any(Enabled)) Notice("That AI is turned off in Settings.");
            aiTurns = 0;
            foreach (var t in targets) Enqueue(t);
            SaveChat();
            PostStatus();
            if (!busy) _ = Pump();
        }

        // Claude and Codex lead (first speaker, or rotating for "alternate"); the backups (DeepSeek, Grok) always go last.
        // auto = a plain message: a backup only joins those when its AutoReply is on (or no main AI is on).
        IEnumerable<string> Order(bool auto = false)
        {
            var main = On().Where(x => !IsBackup(x)).ToList();
            if (main.Count > 0)
            {
                int start = S.FirstSpeaker == "alternate" ? alternate++ % main.Count : Math.Max(0, main.IndexOf(S.FirstSpeaker));
                main = main.Skip(start).Concat(main.Take(start)).ToList();
            }
            bool noMain = main.Count == 0;
            foreach (var b in Backups)
                if (Enabled(b) && (!auto || S.Api(b).AutoReply || noMain)) main.Add(b);
            return main;
        }

        // ---------------------------------------------------------------- backup fallback
        // When Claude or Codex fails, the first backup with Fallback on answers in its place; if that backup also fails,
        // the next one tries. Works even while the backup itself is switched off.
        readonly HashSet<string> forced = new();      // seats allowed to run once even though switched off (fallback)
        readonly HashSet<string> tried = new();       // backups already tried for the current failure
        string fallbackFor, fallbackError;

        static string Short(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 200 ? s.Substring(0, 200) + "…" : s).Replace("\n", " ");

        string NextFallback(string failedId, string err)
        {
            if (string.IsNullOrEmpty(err)) return null;
            if (!IsBackup(failedId)) { tried.Clear(); fallbackFor = failedId; fallbackError = err; }
            else if (fallbackFor == null) return null;                 // a backup failing on its own turn doesn't chain
            else tried.Add(failedId);
            foreach (var b in Backups)
            {
                var a = S.Api(b);
                if (!a.Fallback || tried.Contains(b) || b == failedId || queue.Contains(b)) continue;
                if (a.FallbackOn == "limits" &&
                    !Regex.IsMatch(fallbackError, @"rate|limit|quota|usage|overloaded|529|503|502|timed out|unavailable", RegexOptions.IgnoreCase)) continue;
                return b;
            }
            return null;
        }

        void Stop()
        {
            queue.Clear(); forced.Clear(); tried.Clear(); fallbackFor = fallbackError = null;
            try { cts.Cancel(); } catch { }
            cts = new CancellationTokenSource();
            PostStatus();
        }

        string AccessLine()
        {
            string claudeAccess = S.ClaudeMode switch { "full" => "full access", "edit" => "can edit files in the working folder (no shell commands)", _ => "read-only" };
            string codexAccess = S.CodexSandbox switch { "danger-full-access" => "full access", "workspace-write" => "can edit files and run commands in the working folder", _ => "read-only" };
            var extra = string.IsNullOrWhiteSpace(S.ClaudeExtraDirs) ? "" : $" (Claude's access also covers {S.ClaudeExtraDirs.Trim()})";
            var parts = new List<string>();
            if (S.ClaudeEnabled) parts.Add($"Claude = {claudeAccess}{extra}");
            if (S.CodexEnabled) parts.Add($"Codex = {codexAccess} ({S.WorkDir})");
            foreach (var b in Backups) if (Enabled(b)) parts.Add($"{Display(b)} = chat only (no files or commands)");
            return $"Current access: {string.Join("; ", parts)}.";
        }

        static string Who(string id) => id switch
        {
            "claude" => "Claude (Anthropic, via Claude Code)",
            "codex" => "Codex (OpenAI, via Codex CLI)",
            "deepseek" => "DeepSeek (backup, DeepSeek API, chat only: it cannot read files or run commands)",
            "grok" => "Grok (backup, xAI API, chat only: it cannot read files or run commands)",
            _ => id,
        };

        // ---------------------------------------------------------------- prompts
        // The default wording is the XenoDuo prompt Claude and Codex already work under (generalised to any number of seats).
        // Settings → Prompt can replace either template; {placeholders} are filled in per AI, per turn.
        public const string DefaultPromptTemplate =
            "You are {me} in a live group chat inside XenoStudio, a desktop app {user} built. " +
            "Participants: {participants}. {off}" +
            "New messages from the others are given to you as blocks starting with [Name]:. " +
            "Reply only as yourself in normal markdown. Do not prefix your reply with your name and never write lines for the other participants. " +
            "Keep replies conversational and reasonably short unless {user} asks for depth. " +
            "{handoff}" +
            "Shared working folder: {workdir}. " +
            "Current access, set by {user} in XenoStudio Settings: {access}. " +
            "Plan work around these: don't assume the others have more access than stated. If the job needs more access, tell {user} which setting to change." +
            "{shell}{mcp}{role}";

        public const string DefaultPromptFooter = "(You are {me}. {accessLine} Reply to the conversation above.)";

        public static readonly string[] PromptPlaceholders =
            { "me", "user", "participants", "others", "mentions", "off", "handoff", "workdir", "access", "accessLine", "shell", "mcp", "role", "date", "time" };

        Dictionary<string, string> PromptVars(string id)
        {
            var me = Display(id); var user = S.UserName;
            var others = On().Where(x => x != id).ToList();
            string claudeAccess = S.ClaudeMode switch { "full" => "full access (can run any command and edit any file)", "edit" => "can read and edit files in the working folder, but cannot run shell commands", _ => "read-only (can read/search files and the web; cannot edit files or run commands)" };
            string codexAccess = S.CodexSandbox switch { "danger-full-access" => "full access (can run any command and edit any file)", "workspace-write" => "can edit files and run commands inside the working folder", _ => "read-only (can read files and run read-only commands; cannot write files)" };
            if (!string.IsNullOrWhiteSpace(S.ClaudeExtraDirs)) claudeAccess += $"; Claude's access also covers {S.ClaudeExtraDirs.Trim()}";
            var access = new List<string>();
            if (S.ClaudeEnabled) access.Add($"Claude is {claudeAccess}");
            if (S.CodexEnabled) access.Add($"Codex is {codexAccess}");
            foreach (var b in Backups) if (Enabled(b)) access.Add($"{Display(b)} is chat only (no files, commands or web)");
            var off = Seats.Where(x => x != id && !Enabled(x)).Select(Display).ToList();
            var mentions = string.Join(" / ", others.Select(o => "@" + o));
            var mcp = IsBackup(id) ? new List<string>()
                : (S.McpServers ?? new()).Where(m => m.Enabled && (id == "claude" ? m.Claude : m.Codex)).Select(m => m.Name).ToList();

            string role = "";
            if (IsBackup(id))
            {
                role = " You are a backup participant: Claude and Codex lead. You have no tools: if something needs files, commands or the web, ask Claude or Codex to do it.";
                if (fallbackFor != null)
                    role += $" Right now you are standing in for {Display(fallbackFor)}, whose turn just failed ({Short(fallbackError)}). " +
                            $"Answer what {Display(fallbackFor)} was asked as well as you can without tools, and say briefly that you're covering for them.";
            }

            return new()
            {
                ["me"] = me,
                ["user"] = user,
                ["participants"] = $"{user} (the human user), " + string.Join(", ", new[] { id }.Concat(others).Select(Who)),
                ["others"] = string.Join(", ", others.Select(Display)),
                ["mentions"] = mentions,
                ["off"] = off.Count > 0 ? $"Switched off right now: {string.Join(", ", off)}. " : "",
                ["handoff"] = others.Count > 0 && S.HandoffEnabled
                    ? $"If you want another AI to answer or take something on, mention {mentions} and they will get a turn; only do that when you actually want their reply. " +
                      $"Disagree openly when you think another AI is wrong; that is useful to {user}. "
                    : "",
                ["workdir"] = S.WorkDir,
                ["access"] = string.Join("; ", access),
                ["accessLine"] = AccessLine(),
                ["shell"] = S.ShellEnabled && S.ShellShareWithAi ? $" {user} can run shell commands from the chat with !command; their output appears as [Shell]: blocks." : "",
                ["mcp"] = mcp.Count > 0 ? $" MCP servers XenoStudio gives you: {string.Join(", ", mcp)}." : "",
                ["role"] = role,
                ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["time"] = DateTime.Now.ToString("HH:mm"),
            };
        }

        static string Fill(string template, Dictionary<string, string> vars) =>
            Regex.Replace(template, @"\{(\w+)\}", m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

        string SystemNote(string id)
        {
            var vars = PromptVars(id);
            var sb = new StringBuilder(Fill(string.IsNullOrWhiteSpace(S.PromptTemplate) ? DefaultPromptTemplate : S.PromptTemplate, vars).Trim());
            // the template only replaces the shared wording; these always ride along
            if (!string.IsNullOrWhiteSpace(S.PromptTemplate) && IsBackup(id) && !S.PromptTemplate.Contains("{role}")) sb.Append(vars["role"]);
            if (!string.IsNullOrWhiteSpace(S.SystemExtra)) sb.Append("\n\n").Append(Fill(S.SystemExtra.Trim(), vars));
            var mine = id switch { "claude" => S.ClaudeSystemExtra, "codex" => S.CodexSystemExtra, _ => S.Api(id)?.SystemExtra };
            if (!string.IsNullOrWhiteSpace(mine)) sb.Append("\n\n").Append(Fill(mine.Trim(), vars));
            return sb.ToString();
        }

        string Footer(string id) => Fill(string.IsNullOrWhiteSpace(S.PromptFooter) ? DefaultPromptFooter : S.PromptFooter, PromptVars(id)).Trim();

        string BuildPrompt(string id, AgentState st) => BuildPrompt(id, st, out _);

        /// <param name="images">absolute paths of the pictures in the messages this prompt covers</param>
        string BuildPrompt(string id, AgentState st, out List<string> images)
        {
            var sb = new StringBuilder();
            images = new List<string>();
            for (int i = Math.Max(0, st.Cursor); i < chat.Messages.Count; i++)
            {
                var m = chat.Messages[i];
                var pics = (m.Images ?? new()).Select(UploadPath).Where(p => p != null && File.Exists(p)).ToList();
                if (m.Who == id || m.Who == "system" || m.Error || (string.IsNullOrWhiteSpace(m.Text) && pics.Count == 0)) continue;
                if (m.Who == "shell" && !S.ShellShareWithAi) continue;
                sb.Append('[').Append(Display(m.Who)).Append("]: ").Append(m.Text.Trim());
                foreach (var p in pics) sb.Append(m.Text.Trim().Length > 0 || p != pics[0] ? "\n" : "").Append("[Attached picture: ").Append(p).Append(']');
                sb.Append("\n\n");
                images.AddRange(pics);
            }
            if (sb.Length == 0) return null;
            sb.Append(Footer(id));
            return sb.ToString();
        }
        async Task Pump()
        {
            busy = true;
            var token = cts.Token;
            try
            {
                while (queue.Count > 0 && !token.IsCancellationRequested)
                {
                    if (aiTurns >= S.MaxAiTurns)
                    {
                        queue.Clear();
                        AddMsg(new Msg { Who = "system", Text = $"Paused after {S.MaxAiTurns} AI replies in a row. Send a message to keep going." });
                        break;
                    }
                    var id = queue[0]; queue.RemoveAt(0);
                    bool wasForced = forced.Remove(id);
                    bool isFallback = IsBackup(id) && fallbackFor != null;
                    if (!Enabled(id) && !wasForced) continue;
                    var st = chat.Agents.TryGetValue(id, out var x) ? x : (chat.Agents[id] = new AgentState());
                    var prompt = BuildPrompt(id, st, out var images);
                    if (prompt == null) continue;

                    aiTurns++;
                    running = id; PostStatus();
                    var msg = new Msg { Who = id };
                    chat.Messages.Add(msg);
                    Post(new { type = "start", msg });
                    int seenUpTo = chat.Messages.Count;   // everything before this agent's own message has now been shown to it

                    tracker = new EditTracker(msg.Id, id, ResolveEditPath);   // snapshots files this AI edits during the reply
                    try { tracker.Prime(MentionedPaths(prompt).Concat(chat.Messages.SelectMany(m => m.Files ?? new()).Select(f => f.Path)).Reverse().Distinct(StringComparer.OrdinalIgnoreCase)); }
                    catch (Exception ex) { CrashLog.Write("edit prime", ex); }
                    var r = await RunTurn(id, st, prompt, images, msg, token);
                    if (!r.Ok && !token.IsCancellationRequested && !string.IsNullOrEmpty(st.SessionId) && LooksLikeLostSession(r.Error))
                    {
                        // session vanished on the CLI side: start a fresh one carrying the recent history
                        st.SessionId = null;
                        st.Cursor = Math.Max(0, chat.Messages.Count - 1 - S.ResumeHistory);
                        prompt = BuildPrompt(id, st, out var resumedImages) ?? prompt;
                        if (resumedImages.Count > 0) images = resumedImages;
                        msg.Tools.Clear(); Post(new { type = "reset", id = msg.Id });
                        r = await RunTurn(id, st, prompt, images, msg, token);
                    }
                    // Claude / Codex: retry plain failures (limits, network blips) before giving up or falling back
                    for (int attempt = 1; !r.Ok && !IsBackup(id) && attempt <= S.RetryOnError && !token.IsCancellationRequested; attempt++)
                    {
                        Post(new { type = "tool", id = msg.Id, text = $"retry {attempt}/{S.RetryOnError} after error: {Short(r.Error)}" });
                        try { await Task.Delay(TimeSpan.FromSeconds(S.RetryDelaySec), token); } catch { break; }
                        msg.Tools.Clear(); Post(new { type = "reset", id = msg.Id });
                        r = await RunTurn(id, st, prompt, images, msg, token);
                    }

                    msg.Tools = r.Tools;
                    try { msg.Files = tracker?.Finish() ?? new(); } catch (Exception ex) { CrashLog.Write("edit tracker", ex); }
                    tracker = null;
                    bool chained = false;
                    if (r.Ok)
                    {
                        msg.Text = r.Text;
                        st.SessionId = r.SessionId;
                        st.Cursor = seenUpTo;
                    }
                    else
                    {
                        msg.Error = true;
                        msg.Text = (string.IsNullOrWhiteSpace(r.Text) ? "" : r.Text + "\n\n") + "**Error:** " + r.Error + Hint(id, r.Error);
                        var nb = token.IsCancellationRequested ? null : NextFallback(id, r.Error);
                        if (nb != null)
                        {
                            if (!Enabled(nb)) forced.Add(nb);
                            queue.Insert(0, nb);
                            msg.Text += $"\n\n_{Display(nb)} (backup) is answering in {Display(fallbackFor)}'s place._";
                            chained = true;
                        }
                    }
                    if (!chained && (isFallback || !IsBackup(id))) { fallbackFor = null; fallbackError = null; tried.Clear(); }
                    running = null;
                    chat.Updated = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    Post(new { type = "done", msg });
                    SaveChat();
                    if (S.AutoExport) ExportMarkdown(silent: true);

                    if (r.Ok && S.HandoffEnabled)
                    {
                        foreach (Match m in Mention.Matches(r.Text ?? ""))
                        {
                            var w = m.Groups[1].Value.ToLowerInvariant();
                            if (w is "both" or "all" or "everyone") { foreach (var o in Order().Where(x => w != "both" || !IsBackup(x))) if (o != id) Enqueue(o); }
                            else if (w != id && Seats.Contains(w)) Enqueue(w);
                        }
                    }
                    PostStatus();
                }
            }
            catch (Exception ex) { AddMsg(new Msg { Who = "system", Text = "Error: " + ex.Message, Error = true }); }
            finally
            {
                running = null; busy = false;
                SaveChat();
                Post(new { type = "chats", chats = ListChats() });
                PostStatus();
                // Xeno may have sent a new message while a stopped turn was still shutting down
                if (queue.Count > 0 && !cts.Token.IsCancellationRequested) _ = Pump();
            }
        }

        async Task<AgentResult> RunTurn(string id, AgentState st, string prompt, List<string> images, Msg msg, CancellationToken token)
        {
            var agent = agents[id];
            using var lcts = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (S.TurnTimeoutSec > 0) lcts.CancelAfter(TimeSpan.FromSeconds(S.TurnTimeoutSec));
            var t = lcts.Token;
            AgentResult r;
            try
            {
                r = await Task.Run(() => agent.RunAsync(prompt, st.SessionId, SystemNote(id), S, images, (kind, text) =>
                {
                    if (kind == "tool") Post(new { type = "tool", id = msg.Id, text });
                    else if (kind is "file" or "fileDone")
                    {
                        // an AI is about to change (or just changed) a file: snapshot it and tell the page so it can show "editing…"
                        // Claude announces an edit before running it (snapshot now is accurate); Codex's announcement
                        // can arrive after the patch landed, so it only counts if the file was primed before the turn
                        var full = kind == "fileDone" ? tracker?.After(text) : id == "claude" ? tracker?.Before(text) : tracker?.Late(text);
                        if (full != null) Post(new { type = "aiFile", id = msg.Id, who = id, path = full, phase = kind == "file" ? "editing" : "edited" });
                    }
                    else Post(new { type = "delta", id = msg.Id, text });
                }, t));
            }
            catch (Exception ex)
            {
                // a crash inside one AI's turn becomes that turn's error, so retry / fallback / the next AI still run
                CrashLog.Write("turn " + id, ex);
                r = new AgentResult { Error = ex is OperationCanceledException ? "Stopped." : ex.Message, SessionId = st.SessionId };
            }
            if (!r.Ok && t.IsCancellationRequested && !token.IsCancellationRequested)
                r.Error = $"Timed out after {S.TurnTimeoutSec}s (Settings → Turn time limit).";
            return r;
        }

        static bool LooksLikeLostSession(string e) =>
            e != null && Regex.IsMatch(e, @"no conversation found|session.*not found|thread.*not found|could not find (the )?(session|thread|rollout)|no rollout", RegexOptions.IgnoreCase);

        string Hint(string id, string e)
        {
            e ??= "";
            if (IsBackup(id))
            {
                var a = S.Api(id);
                if (Regex.IsMatch(e, @"api key|401|403|auth|invalid.*key", RegexOptions.IgnoreCase))
                    return $"\n\nAdd your API key from {a.Site} in Settings → {a.Name} (or set {a.KeyEnv}). Or turn {a.Name} off there.";
                if (Regex.IsMatch(e, @"402|insufficient|balance|credit", RegexOptions.IgnoreCase))
                    return $"\n\nYour {a.Name} API balance is empty. Top up at {a.Site}, or turn {a.Name} off in Settings.";
                if (Regex.IsMatch(e, @"404|model.*not|not.*model", RegexOptions.IgnoreCase))
                    return $"\n\nThe model name in Settings → {a.Name} isn't available on that API. Check the model list at {a.Site}.";
                if (Regex.IsMatch(e, @"connect|refused|actively|host", RegexOptions.IgnoreCase))
                    return $"\n\nCouldn't reach the API address in Settings → {a.Name}. For Ollama, make sure it's running.";
                return "";
            }
            if (Regex.IsMatch(e, @"cannot find|not recognized|No such file|Win32Exception|system cannot find", RegexOptions.IgnoreCase))
                return id == "claude"
                    ? "\n\nClaude Code isn't installed where XenoStudio looked. Install it with `npm i -g @anthropic-ai/claude-code`, or set its path in Settings."
                    : "\n\nCodex CLI wasn't found. Open the ChatGPT/Codex desktop app once, or set codex.exe's path in Settings.";
            if (Regex.IsMatch(e, @"log ?in|logged|auth|credential|api key|401|unauthori", RegexOptions.IgnoreCase))
                return id == "claude"
                    ? "\n\nSign in once: open a terminal, run `claude`, and log in with your Claude account (Pro). Then send again."
                    : "\n\nSign in once: run `codex login` in a terminal (or sign in inside the ChatGPT desktop app). Then send again.";
            if (Regex.IsMatch(e, @"rate|limit|quota|usage", RegexOptions.IgnoreCase))
                return "\n\nLooks like a plan usage limit. It resets on the provider's schedule.";
            return "";
        }

        // ---------------------------------------------------------------- persistence
        record ChatInfo(string Id, string Title, long Updated, int Count);

        List<ChatInfo> ListChats()
        {
            var list = new List<ChatInfo>();
            try
            {
                foreach (var f in Directory.GetFiles(ChatDir, "*.json"))
                {
                    try
                    {
                        var c = JsonSerializer.Deserialize<Chat>(File.ReadAllText(f), J);
                        if (c != null && c.Messages.Count > 0) list.Add(new ChatInfo(c.Id, c.Title, c.Updated, c.Messages.Count));
                    }
                    catch { }
                }
            }
            catch { }
            return list.OrderByDescending(c => c.Updated).ToList();
        }

        static Chat LoadChat(string id)
        {
            try { return JsonSerializer.Deserialize<Chat>(File.ReadAllText(Path.Combine(ChatDir, id + ".json")), J); } catch { return null; }
        }

        void SaveChat()
        {
            if (chat.Messages.Count == 0) return;
            try
            {
                var path = Path.Combine(ChatDir, chat.Id + ".json");
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(chat, J));
                File.Move(tmp, path, true);
            }
            catch { }
        }

        void ExportMarkdown(bool silent = false)
        {
            if (chat.Messages.Count == 0) { if (!silent) Notice("Nothing to export yet."); return; }
            try
            {
                var dir = Path.Combine(S.PrimaryWorkDir, "XenoStudio chats");
                Directory.CreateDirectory(dir);
                var safe = Regex.Replace(chat.Title, @"[^\w\- ]+", "").Trim();
                if (safe.Length > 50) safe = safe.Substring(0, 50).Trim();
                var path = Path.Combine(dir, $"{chat.Id} {safe}.md");
                var sb = new StringBuilder($"# {chat.Title}\n\n");
                foreach (var m in chat.Messages)
                {
                    var when = DateTimeOffset.FromUnixTimeMilliseconds(m.Ts).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                    sb.Append($"## {Display(m.Who)} · {when}\n\n");
                    if (m.Tools.Count > 0) sb.Append(string.Join("\n", m.Tools.Select(t => $"> `{t}`"))).Append("\n\n");
                    sb.Append(m.Text.Trim()).Append("\n\n");
                    foreach (var img in m.Images ?? new()) sb.Append($"![picture]({UploadPath(img)?.Replace('\\', '/')})\n\n");
                }
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                if (silent) return;
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                Notice("Exported to " + path);
            }
            catch (Exception ex) { if (!silent) Notice("Export failed: " + ex.Message); }
        }
    }
}
