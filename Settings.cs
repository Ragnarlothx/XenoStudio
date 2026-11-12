using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace XenoStudio
{
    public class McpServer
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "stdio";              // stdio | http | sse
        public string Command { get; set; } = "";
        public List<string> Args { get; set; } = new();
        public string Url { get; set; } = "";
        public Dictionary<string, string> Env { get; set; } = new();
        public Dictionary<string, string> Headers { get; set; } = new();
        public bool Enabled { get; set; } = true;
        public bool Claude { get; set; } = true;                 // give this server to Claude
        public bool Codex { get; set; } = true;                  // give this server to Codex
    }

    public class ApiSeat
    {
        public string Id, Name, ApiKey, BaseUrl, Model, SystemExtra, FallbackOn, KeyEnv, KeyHost, Site;
        public bool Enabled, ShowReasoning, AutoReply, Fallback;
        public int MaxTokens;
    }

    public class Settings
    {
        // ---- general
        public string UserName { get; set; } = "Xeno";
        public string WorkDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public string SystemExtra { get; set; } = "";            // extra instructions given to both AIs
        public string PromptTemplate { get; set; } = "";         // full shared system prompt with {placeholders}; blank = built-in default
        public string PromptFooter { get; set; } = "";           // line appended after each turn's [Name]: blocks; blank = built-in default
        public int MaxAiTurns { get; set; } = 6;                 // AI replies in a row before waiting for the user
        public string FirstSpeaker { get; set; } = "claude";     // claude | codex | alternate
        public int TurnTimeoutSec { get; set; } = 0;             // 0 = no limit
        public int RetryOnError { get; set; } = 1;             // retry a failed Claude/Codex turn this many times (before any fallback)
        public int RetryDelaySec { get; set; } = 5;
        public bool HandoffEnabled { get; set; } = true;         // an AI's @mention gives the other AI a turn
        public int ResumeHistory { get; set; } = 30;             // messages re-sent when a lost session has to be restarted
        public bool AutoExport { get; set; } = false;            // keep a markdown copy of every chat in <WorkDir>\XenoStudio chats
        public bool ShellEnabled { get; set; } = true;           // "!command" in the composer runs it in WorkDir
        public string Shell { get; set; } = "powershell";        // powershell | pwsh | cmd
        public bool ShellShareWithAi { get; set; } = true;       // shell output is shown to the AIs on their next turn
        public string XenoEditPath { get; set; } = "";             // XenoEdit.exe for "Open in XenoEdit"; blank = find it automatically
        public string Theme { get; set; } = "dark";              // dark | light | cyberpunk | hacker (CSS in web\, window chrome in ThemeChrome)

        // ---- Claude
        public bool ClaudeEnabled { get; set; } = true;
        public bool ClaudeUseBundled { get; set; } = true;       // runtime\claude.exe next to XenoStudio.exe
        public string ClaudePath { get; set; } = "";
        public string ClaudeModel { get; set; } = "";
        public string ClaudeFallbackModel { get; set; } = "";
        public string ClaudeMode { get; set; } = "read";         // read | edit | full
        public string ClaudeExtraDirs { get; set; } = "";        // , or ; separated (--add-dir)
        public string ClaudeAllowedTools { get; set; } = "";     // comma-separated, added to --allowedTools
        public string ClaudeDisallowedTools { get; set; } = "";  // comma-separated (--disallowedTools)
        public int ClaudeMaxTurns { get; set; } = 0;             // agent steps per reply, 0 = CLI default
        public bool ClaudeStrictMcp { get; set; } = false;       // only XenoStudio's MCP servers, ignore ~/.claude.json ones
        public bool ClaudeAutoAllowMcp { get; set; } = true;     // pre-approve tools of XenoStudio's MCP servers
        public string ClaudeSystemExtra { get; set; } = "";
        public string ClaudeExtraArgs { get; set; } = "";        // raw CLI args, quotes allowed
        public string ClaudeEnv { get; set; } = "";              // KEY=VALUE per line

        // ---- Codex
        public bool CodexEnabled { get; set; } = true;
        public bool CodexUseBundled { get; set; } = true;        // runtime\codex.exe next to XenoStudio.exe
        public string CodexPath { get; set; } = "";
        public string CodexModel { get; set; } = "";
        public string CodexReasoning { get; set; } = "";         // "" | minimal | low | medium | high
        public string CodexSandbox { get; set; } = "read-only";  // read-only | workspace-write | danger-full-access
        public bool CodexUseConfig { get; set; } = false;        // also load ~/.codex/config.toml
        public string CodexSystemExtra { get; set; } = "";
        public string CodexExtraArgs { get; set; } = "";
        public string CodexEnv { get; set; } = "";

        // ---- DeepSeek (chat-only, OpenAI-compatible HTTP API; also works with Ollama / LM Studio / any compatible server)
        public bool DeepSeekEnabled { get; set; } = false;
        public string DeepSeekApiKey { get; set; } = "";          // blank = DEEPSEEK_API_KEY env var; local servers need none
        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
        public string DeepSeekModel { get; set; } = "deepseek-chat";   // deepseek-chat | deepseek-reasoner | any model the server has
        public int DeepSeekMaxTokens { get; set; } = 0;           // 0 = server default
        public bool DeepSeekShowReasoning { get; set; } = false;  // show reasoner "thinking" as a tool line
        public string DeepSeekSystemExtra { get; set; } = "";
        public bool DeepSeekAutoReply { get; set; } = false;      // when on as a member: also answer plain messages (always last). Off = only @deepseek / its chip
        public bool DeepSeekFallback { get; set; } = false;       // stand in when Claude or Codex fails — works even while DeepSeek itself is off
        public string DeepSeekFallbackOn { get; set; } = "any";   // any | limits (usage limits / outages only, not sign-in or setup errors)

        // ---- Grok (xAI, OpenAI-compatible HTTP API; same backup role as DeepSeek)
        public bool GrokEnabled { get; set; } = false;
        public string GrokApiKey { get; set; } = "";              // blank = XAI_API_KEY env var
        public string GrokBaseUrl { get; set; } = "https://api.x.ai/v1";
        public string GrokModel { get; set; } = "grok-4";
        public int GrokMaxTokens { get; set; } = 0;
        public bool GrokShowReasoning { get; set; } = false;
        public string GrokSystemExtra { get; set; } = "";
        public bool GrokAutoReply { get; set; } = false;
        public bool GrokFallback { get; set; } = false;
        public string GrokFallbackOn { get; set; } = "any";

        /// <summary>The backup (API) seats share one shape; this maps a seat id onto its flat settings fields.</summary>
        public ApiSeat Api(string id) => id switch
        {
            "deepseek" => new ApiSeat
            {
                Id = id, Name = "DeepSeek", Enabled = DeepSeekEnabled, ApiKey = DeepSeekApiKey, BaseUrl = DeepSeekBaseUrl, Model = DeepSeekModel,
                MaxTokens = DeepSeekMaxTokens, ShowReasoning = DeepSeekShowReasoning, SystemExtra = DeepSeekSystemExtra,
                AutoReply = DeepSeekAutoReply, Fallback = DeepSeekFallback, FallbackOn = DeepSeekFallbackOn,
                KeyEnv = "DEEPSEEK_API_KEY", KeyHost = "api.deepseek.com", Site = "platform.deepseek.com",
            },
            "grok" => new ApiSeat
            {
                Id = id, Name = "Grok", Enabled = GrokEnabled, ApiKey = GrokApiKey, BaseUrl = GrokBaseUrl, Model = GrokModel,
                MaxTokens = GrokMaxTokens, ShowReasoning = GrokShowReasoning, SystemExtra = GrokSystemExtra,
                AutoReply = GrokAutoReply, Fallback = GrokFallback, FallbackOn = GrokFallbackOn,
                KeyEnv = "XAI_API_KEY", KeyHost = "api.x.ai", Site = "console.x.ai",
            },
            _ => null,
        };

        // ---- MCP servers shared by both AIs
        public List<McpServer> McpServers { get; set; } = new();

        // ---- free-form UI preferences (font size, accent, sounds…) owned by web\app.js; the host just stores them
        public JsonObject Ui { get; set; } = new();

        // ---- folder lists. WorkDir and ClaudeExtraDirs may hold several folders separated by , ; or |
        // ("D:\Projects,E:\Work"). The first existing WorkDir folder is where the CLIs start; the rest are added with --add-dir.
        public static List<string> SplitDirs(string s) =>
            (s ?? "").Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(d => d.Trim('"')).Where(d => d != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        [System.Text.Json.Serialization.JsonIgnore]
        public string PrimaryWorkDir =>
            SplitDirs(WorkDir).FirstOrDefault(Directory.Exists) ?? Paths.Base;

        /// <summary>Every existing folder the AIs should reach besides PrimaryWorkDir.</summary>
        public List<string> ExtraDirs(bool includeClaudeExtras)
        {
            var all = SplitDirs(WorkDir);
            if (includeClaudeExtras) all.AddRange(SplitDirs(ClaudeExtraDirs));
            var primary = PrimaryWorkDir;
            return all.Where(Directory.Exists)
                      .Where(d => !string.Equals(Path.GetFullPath(d).TrimEnd('\\'), Path.GetFullPath(primary).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                      .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// MCP servers copied from the Codex/Claude desktop apps carry install paths that change when those apps update
        /// (node_repl's runtime folder, CODEX_CLI_PATH…). When a stored server points at a file or folder that no longer
        /// exists, re-read it from the app's own config, keeping the user's on/off and Claude/Codex choices.
        /// </summary>
        public List<string> RefreshStaleMcp()
        {
            var refreshed = new List<string>();
            var stale = (McpServers ?? new()).Where(m => m.Type == "stdio" && IsStale(m)).ToList();
            if (stale.Count == 0) return refreshed;
            var fresh = Util.ImportMcp();
            foreach (var m in stale)
            {
                var f = fresh.FirstOrDefault(x => x.Type == "stdio" && string.Equals(x.Name, m.Name, StringComparison.OrdinalIgnoreCase) && !IsStale(x));
                if (f == null) continue;
                m.Command = f.Command; m.Args = f.Args; m.Env = f.Env;
                refreshed.Add(m.Name);
            }
            return refreshed;
        }

        static bool IsStale(McpServer m)
        {
            static bool Missing(string p)
            {
                p = (p ?? "").Trim().Trim('"');
                if (p.Length < 3 || p.StartsWith(@"\\") || p.Contains(';') || !Path.IsPathRooted(p) || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
                return !File.Exists(p) && !Directory.Exists(p);
            }
            return Missing(m.Command) || m.Env.Values.Any(Missing);
        }

        // ---- one-time upgrades of saved settings when a newer XenoStudio first opens them
        public List<string> SkipLogin { get; set; } = new();   // "claude"/"codex": user said it's signed in already; don't check or nag
        public bool SetupDone { get; set; } = false;    // first-run setup finished (or everything was already in place)
        public int SettingsVersion { get; set; } = 0;   // 0 = saved by 1.1.0 or older (the field didn't exist)

        public void Clamp()
        {
            if (SettingsVersion < 2)
            {
                // 1.2.0: retry a failed Claude/Codex turn once by default (network blips, brief overloads)
                if (RetryOnError == 0) RetryOnError = 1;
                SettingsVersion = 2;
            }
            MaxAiTurns = Math.Clamp(MaxAiTurns, 1, 50);
            TurnTimeoutSec = Math.Clamp(TurnTimeoutSec, 0, 24 * 3600);
            ClaudeMaxTurns = Math.Clamp(ClaudeMaxTurns, 0, 500);
            RetryOnError = Math.Clamp(RetryOnError, 0, 5);
            RetryDelaySec = Math.Clamp(RetryDelaySec, 0, 300);
            ResumeHistory = Math.Clamp(ResumeHistory, 1, 500);
            if (Theme is not ("dark" or "light" or "cyberpunk" or "hacker")) Theme = "dark";
            if (string.IsNullOrWhiteSpace(UserName)) UserName = "Xeno";
            UserName = UserName.Trim();
            DeepSeekMaxTokens = Math.Clamp(DeepSeekMaxTokens, 0, 1_000_000);
            if (string.IsNullOrWhiteSpace(DeepSeekBaseUrl)) DeepSeekBaseUrl = "https://api.deepseek.com";
            if (string.IsNullOrWhiteSpace(DeepSeekModel)) DeepSeekModel = "deepseek-chat";
            if (FirstSpeaker is not ("claude" or "codex" or "alternate")) FirstSpeaker = "claude";   // DeepSeek always goes last
            if (DeepSeekFallbackOn is not ("any" or "limits")) DeepSeekFallbackOn = "any";
            GrokMaxTokens = Math.Clamp(GrokMaxTokens, 0, 1_000_000);
            if (string.IsNullOrWhiteSpace(GrokBaseUrl)) GrokBaseUrl = "https://api.x.ai/v1";
            if (string.IsNullOrWhiteSpace(GrokModel)) GrokModel = "grok-4";
            if (GrokFallbackOn is not ("any" or "limits")) GrokFallbackOn = "any";
            McpServers ??= new(); Ui ??= new();
            McpServers.RemoveAll(m => string.IsNullOrWhiteSpace(m.Name));
            foreach (var m in McpServers)
            {
                m.Name = Regex.Replace(m.Name.Trim(), @"[^\w\-]", "_");
                m.Args ??= new(); m.Env ??= new(); m.Headers ??= new();
                m.Type = m.Type is "http" or "sse" ? m.Type : "stdio";
            }
        }
    }

    /// <summary>Everything XenoStudio writes lives next to the exe, so the whole folder can be zipped and shared.</summary>
    public static class Paths
    {
        public static readonly string Base = AppContext.BaseDirectory;
        public static readonly string Data = Path.Combine(Base, "data");
        public static readonly string Chats = Path.Combine(Data, "chats");
        public static readonly string SettingsFile = Path.Combine(Data, "settings.json");
        public static readonly string Profiles = Path.Combine(Data, "profiles");
        public static readonly string ClaudeSettingsProfile = Path.Combine(Profiles, "claude-settings.json");   // passed with --settings when present
        public static readonly string ClaudeMcpFile = Path.Combine(Data, "mcp-claude.json");                    // generated each turn
        public static readonly string Sessions = Path.Combine(Data, "sessions");                                // DeepSeek history (API is stateless)
        public static readonly string Uploads = Path.Combine(Data, "uploads");                                  // pictures attached in the composer, per chat
        public static readonly string Runtime = Path.Combine(Base, "runtime");
        public static readonly string BundledClaude = Path.Combine(Runtime, "claude.exe");
        public static readonly string BundledCodex = Path.Combine(Runtime, "codex.exe");
        public static readonly string OldXenoDuoSettings =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XenoDuo", "settings.json");
    }

    public static class Util
    {
        /// <summary>Splits a command line the way a user types it: spaces separate, "double quotes" group.</summary>
        public static List<string> SplitArgs(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            var cur = new StringBuilder(); bool q = false, any = false;
            foreach (var ch in s)
            {
                if (ch == '"') { q = !q; any = true; }
                else if (char.IsWhiteSpace(ch) && !q) { if (any || cur.Length > 0) list.Add(cur.ToString()); cur.Clear(); any = false; }
                else cur.Append(ch);
            }
            if (any || cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        /// <summary>KEY=VALUE lines (blank lines and # comments ignored).</summary>
        public static Dictionary<string, string> ParseEnv(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (s ?? "").Split('\n'))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int i = l.IndexOf('=');
                if (i > 0) d[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
            return d;
        }

        static string TomlStr(string s) => JsonSerializer.Serialize(s ?? "");   // JSON string escaping is valid TOML basic-string escaping
        static string TomlKey(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9_\-]+$") ? s : TomlStr(s);

        /// <summary>Codex "-c key=value" overrides that add XenoStudio's MCP servers for one run.</summary>
        public static List<string> CodexMcpOverrides(IEnumerable<McpServer> servers)
        {
            var a = new List<string>();
            foreach (var m in servers)
            {
                var k = "mcp_servers." + TomlKey(m.Name);
                if (m.Type == "stdio")
                {
                    a.Add("-c"); a.Add($"{k}.command={TomlStr(m.Command)}");
                    a.Add("-c"); a.Add($"{k}.args=[{string.Join(",", m.Args.Select(TomlStr))}]");
                    if (m.Env.Count > 0) { a.Add("-c"); a.Add($"{k}.env={{{string.Join(",", m.Env.Select(e => TomlKey(e.Key) + "=" + TomlStr(e.Value)))}}}"); }
                }
                else
                {
                    a.Add("-c"); a.Add($"{k}.url={TomlStr(m.Url)}");
                    if (m.Headers.Count > 0) { a.Add("-c"); a.Add($"{k}.http_headers={{{string.Join(",", m.Headers.Select(e => TomlKey(e.Key) + "=" + TomlStr(e.Value)))}}}"); }
                }
            }
            return a;
        }

        /// <summary>Claude Code --mcp-config file contents.</summary>
        public static string ClaudeMcpJson(IEnumerable<McpServer> servers)
        {
            var root = new JsonObject();
            var ms = new JsonObject();
            foreach (var m in servers)
            {
                var o = new JsonObject { ["type"] = m.Type };
                if (m.Type == "stdio")
                {
                    o["command"] = m.Command;
                    o["args"] = new JsonArray(m.Args.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());
                    if (m.Env.Count > 0) o["env"] = JsonSerializer.SerializeToNode(m.Env);
                }
                else
                {
                    o["url"] = m.Url;
                    if (m.Headers.Count > 0) o["headers"] = JsonSerializer.SerializeToNode(m.Headers);
                }
                ms[m.Name] = o;
            }
            root["mcpServers"] = ms;
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>Reads MCP servers from the user's existing Claude Code (~/.claude.json) and Codex (~/.codex/config.toml) setups.</summary>
        public static List<McpServer> ImportMcp()
        {
            var found = new List<McpServer>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            try
            {
                var f = Path.Combine(home, ".claude.json");
                if (File.Exists(f) && JsonNode.Parse(File.ReadAllText(f))?["mcpServers"] is JsonObject cm)
                    foreach (var kv in cm)
                    {
                        var o = kv.Value;
                        var m = new McpServer { Name = kv.Key, Codex = true, Claude = true };
                        var t = o?["type"]?.ToString();
                        m.Type = t is "http" or "sse" ? t : "stdio";
                        m.Command = o?["command"]?.ToString() ?? "";
                        m.Url = o?["url"]?.ToString() ?? "";
                        if (o?["args"] is JsonArray aa) m.Args = aa.Select(x => x?.ToString() ?? "").ToList();
                        if (o?["env"] is JsonObject ee) foreach (var e in ee) m.Env[e.Key] = e.Value?.ToString() ?? "";
                        if (o?["headers"] is JsonObject hh) foreach (var e in hh) m.Headers[e.Key] = e.Value?.ToString() ?? "";
                        found.Add(m);
                    }
            }
            catch { }

            // minimal TOML reader: [mcp_servers.NAME] / [mcp_servers.NAME.env] tables with string, array and inline-table values
            try
            {
                var f = Path.Combine(home, ".codex", "config.toml");
                if (File.Exists(f))
                {
                    McpServer cur = null; string sub = null;
                    foreach (var raw in File.ReadAllLines(f))
                    {
                        var l = raw.Trim();
                        if (l.Length == 0 || l.StartsWith("#")) continue;
                        var sec = Regex.Match(l, @"^\[mcp_servers\.(""[^""]+""|[\w\-]+)(?:\.(\w+))?\]$");
                        if (sec.Success)
                        {
                            var name = sec.Groups[1].Value.Trim('"');
                            sub = sec.Groups[2].Success ? sec.Groups[2].Value : null;
                            cur = found.FirstOrDefault(x => x.Name == name);
                            if (cur == null) { cur = new McpServer { Name = name }; found.Add(cur); }
                            continue;
                        }
                        if (l.StartsWith("[")) { cur = null; continue; }
                        if (cur == null) continue;
                        var kv = Regex.Match(l, @"^(""[^""]+""|[\w\-]+)\s*=\s*(.+)$");
                        if (!kv.Success) continue;
                        var key = kv.Groups[1].Value.Trim('"'); var val = kv.Groups[2].Value.Trim();
                        if (sub == "env") { cur.Env[key] = TomlValue(val); continue; }
                        if (sub == "http_headers") { cur.Headers[key] = TomlValue(val); continue; }
                        if (sub != null) continue;
                        switch (key)
                        {
                            case "command": cur.Command = TomlValue(val); cur.Type = "stdio"; break;
                            case "url": cur.Url = TomlValue(val); cur.Type = "http"; break;
                            case "args":
                                try { cur.Args = JsonSerializer.Deserialize<List<string>>(Regex.Replace(val, @"'([^']*)'", m => JsonSerializer.Serialize(m.Groups[1].Value))); } catch { }
                                break;
                            case "env":
                                foreach (Match e in Regex.Matches(val, @"(""[^""]+""|[\w\-]+)\s*=\s*(""(?:[^""\\]|\\.)*""|'[^']*')"))
                                    cur.Env[e.Groups[1].Value.Trim('"')] = TomlValue(e.Groups[2].Value);
                                break;
                        }
                    }
                }
            }
            catch { }
            return found;
        }

        static string TomlValue(string v)
        {
            v = v.Trim();
            if (v.StartsWith("'")) return v.Trim('\'');
            if (v.StartsWith("\"")) { try { return JsonSerializer.Deserialize<string>(v); } catch { return v.Trim('"'); } }
            return v;
        }
    }
}
