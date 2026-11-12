using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace XenoStudio
{
    /// <summary>One row of the setup checklist (tools, CLIs, sign-ins).</summary>
    public class SetupItem
    {
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";       // tool | cli | login
        public string Name { get; set; } = "";
        public string Why { get; set; } = "";         // one line: what it's for
        public bool Required { get; set; }            // the app can't do its main job without it
        public bool Ok { get; set; }
        public string Status { get; set; } = "";      // ok | missing | warn
        public string Detail { get; set; } = "";      // version / account type / what's wrong
        public List<string> Actions { get; set; } = new();   // install | login | apikey | open
        public string Url { get; set; }               // for "open": a download page when there's no automatic install
    }

    /// <summary>A recommended MCP server XenoStudio can add and test for you.</summary>
    public class McpCatalogEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Desc { get; set; } = "";
        public string Needs { get; set; } = "";       // node | uv
        public bool Installed { get; set; }
        public Func<McpServer> Make;
    }

    /// <summary>
    /// First-run setup: finds what's installed and signed in, installs what's missing (only after the user confirms),
    /// opens CLI sign-ins, and adds tested MCP servers. Read-only checks never change anything on the PC.
    /// </summary>
    public static class Setup
    {
        // ------------------------------------------------------------------------------------- PATH helpers
        /// <summary>Installers update PATH in the registry; pull those changes into this process so new tools are found without a restart.</summary>
        public static void RefreshPath()
        {
            try
            {
                var parts = new List<string>();
                foreach (var t in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Process })
                    parts.AddRange((Environment.GetEnvironmentVariable("Path", t) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                parts.Add(Path.Combine(home, ".local", "bin"));                                         // Claude Code native installer, uv
                parts.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));   // npm -g
                Environment.SetEnvironmentVariable("Path", string.Join(";", parts.Select(Environment.ExpandEnvironmentVariables).Distinct(StringComparer.OrdinalIgnoreCase)));
            }
            catch { }
        }

        public static string Which(string name)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                foreach (var ext in new[] { ".exe", ".cmd" })
                    try { var p = Path.Combine(dir, name + ext); if (File.Exists(p)) return p; } catch { }
            return null;
        }

        /// <summary>Runs a command hidden and captures its output (stdout + stderr). Cmd shims (.cmd) go through cmd.exe.</summary>
        public static async Task<(int code, string output)> Run(string exe, IEnumerable<string> args, int timeoutMs = 20000,
                                                               string stdin = null, Action<string> onLine = null, CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                                             RedirectStandardInput = stdin != null, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.ArgumentList.Add("/c"); psi.ArgumentList.Add(exe);
            }
            else psi.FileName = exe;
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["DISABLE_AUTOUPDATER"] = "1";
            psi.Environment["NO_COLOR"] = "1";

            var sb = new StringBuilder();
            using var p = new Process { StartInfo = psi };
            void Line(string l) { if (l == null) return; lock (sb) sb.AppendLine(l); try { onLine?.Invoke(l); } catch { } }
            p.OutputDataReceived += (_, e) => Line(e.Data);
            p.ErrorDataReceived += (_, e) => Line(e.Data);
            p.Start();
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if (stdin != null) { try { await p.StandardInput.WriteAsync(stdin); p.StandardInput.Close(); } catch (IOException) { } }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return (-1, sb + "\n(timed out)"); }
            p.WaitForExit();   // flush the async readers
            return (p.ExitCode, sb.ToString());
        }

        static async Task<string> Version(string exe, params string[] args)
        {
            if (exe == null) return null;
            try
            {
                var (code, o) = await Run(exe, args.Length > 0 ? args : new[] { "--version" }, 15000);
                var first = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l != "");
                return code == 0 ? first : null;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------------------------- scan
        public static bool HasWinget => Which("winget") != null;

        public static async Task<List<SetupItem>> Scan(Settings s, string claudeExe, string codexExe)
        {
            RefreshPath();
            var t = new[]
            {
                Task.Run(() => ScanTool("node", "Node.js", "Installs Codex and runs MCP servers started with npx.", "https://nodejs.org/en/download")),
                Task.Run(() => ScanTool("git", "Git for Windows", "Claude Code uses Git Bash on Windows to run commands.", "https://git-scm.com/download/win")),
                Task.Run(() => ScanTool("uv", "uv (Python runner)", "Runs Python-based MCP servers such as Windows-MCP (uvx).", "https://docs.astral.sh/uv/")),
                Task.Run(() => ScanCli("claude", claudeExe, s.ClaudeEnabled)),
                Task.Run(() => ScanCli("codex", codexExe, s.CodexEnabled)),
                Task.Run(() => ScanLogin("claude", claudeExe, s)),
                Task.Run(() => ScanLogin("codex", codexExe, s)),
            };
            return (await Task.WhenAll(t)).ToList();
        }

        public static Task<SetupItem> ScanOne(string id, Settings s, string claudeExe, string codexExe)
        {
            RefreshPath();
            return id switch
            {
                "node" => ScanTool("node", "Node.js", "Installs Codex and runs MCP servers started with npx.", "https://nodejs.org/en/download"),
                "git" => ScanTool("git", "Git for Windows", "Claude Code uses Git Bash on Windows to run commands.", "https://git-scm.com/download/win"),
                "uv" => ScanTool("uv", "uv (Python runner)", "Runs Python-based MCP servers such as Windows-MCP (uvx).", "https://docs.astral.sh/uv/"),
                "claude" => ScanCli("claude", claudeExe, s.ClaudeEnabled),
                "codex" => ScanCli("codex", codexExe, s.CodexEnabled),
                "claude-login" => ScanLogin("claude", claudeExe, s),
                "codex-login" => ScanLogin("codex", codexExe, s),
                _ => throw new ArgumentException("unknown setup item " + id),
            };
        }

        static async Task<SetupItem> ScanTool(string id, string name, string why, string url)
        {
            var exe = Which(id);
            var ver = await Version(exe);
            var it = new SetupItem { Id = id, Group = "tool", Name = name, Why = why, Required = false, Ok = ver != null, Url = url };
            it.Status = it.Ok ? "ok" : "missing";
            it.Detail = it.Ok ? ver : "Not installed";
            if (!it.Ok) it.Actions.Add(HasWinget ? "install" : "open");
            return it;
        }

        static async Task<SetupItem> ScanCli(string id, string exe, bool enabled)
        {
            bool exists = exe != null && Path.IsPathRooted(exe) && File.Exists(exe);
            var ver = exists ? await Version(exe) : null;
            var it = new SetupItem
            {
                Id = id, Group = "cli", Required = enabled,
                Name = id == "claude" ? "Claude Code CLI" : "Codex CLI",
                Why = id == "claude" ? "Runs Claude. Uses your Claude plan (Pro / Max) or an Anthropic API key."
                                     : "Runs Codex. Uses your ChatGPT plan or an OpenAI API key.",
                Url = id == "claude" ? "https://docs.claude.com/en/docs/claude-code/setup" : "https://developers.openai.com/codex/cli",
                Ok = ver != null,
            };
            it.Status = it.Ok ? "ok" : exists ? "warn" : "missing";
            it.Detail = it.Ok ? $"{ver} · {Shorten(exe)}" : exists ? "Found but won't start: " + Shorten(exe) : "Not installed";
            if (!it.Ok) it.Actions.Add("install");
            if (!enabled) it.Detail += " (turned off in Settings)";
            return it;
        }

        static async Task<SetupItem> ScanLogin(string id, string exe, Settings s)
        {
            var it = new SetupItem
            {
                Id = id + "-login", Group = "login", Required = id == "claude" ? s.ClaudeEnabled : s.CodexEnabled,
                Name = id == "claude" ? "Claude sign-in" : "Codex sign-in",
                Why = id == "claude" ? "Signs Claude Code in to your Claude account (browser), or use an Anthropic API key."
                                     : "Signs Codex in with ChatGPT (browser), or use an OpenAI API key.",
            };
            bool exists = exe != null && Path.IsPathRooted(exe) && File.Exists(exe);
            if (!exists) { it.Status = "missing"; it.Detail = "Install the CLI first"; return it; }

            if (id == "claude")
            {
                if (Util.ParseEnv(s.ClaudeEnv).TryGetValue("ANTHROPIC_API_KEY", out var k) && !string.IsNullOrWhiteSpace(k))
                { it.Ok = true; it.Detail = "API key saved in XenoStudio settings"; }
                else
                {
                    var (code, o) = await Run(exe, new[] { "auth", "status" }, 20000);
                    try
                    {
                        var j = JsonNode.Parse(o.Substring(Math.Max(0, o.IndexOf('{'))));
                        if ((bool?)j?["loggedIn"] == true)
                        {
                            it.Ok = true;
                            var method = (string)j["authMethod"] ?? "";
                            it.Detail = method == "claude.ai" ? "Signed in with a Claude account" : "Signed in (" + method + ")";
                        }
                        else it.Detail = "Not signed in";
                    }
                    catch { it.Detail = code == 0 ? "Couldn't read sign-in status" : "Not signed in"; }
                }
            }
            else
            {
                var (code, o) = await Run(exe, new[] { "login", "status" }, 20000);
                var line = o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Logged in", StringComparison.OrdinalIgnoreCase));
                it.Ok = code == 0 && line != null;
                it.Detail = it.Ok ? line.Replace("Logged in using", "Signed in with") : "Not signed in";
            }
            it.Status = it.Ok ? "ok" : "missing";
            if (!it.Ok) { it.Actions.Add("login"); it.Actions.Add("apikey"); }
            return it;
        }

        static string Shorten(string p)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return !string.IsNullOrEmpty(home) && p.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + p[home.Length..] : p;
        }

        // ------------------------------------------------------------------------------------- install
        /// <summary>What an install will run, in words, for the confirmation prompt. Null if there's nothing to run.</summary>
        public static (string what, List<string> needsFirst) Plan(string id)
        {
            var first = new List<string>();
            switch (id)
            {
                case "node": return ("Node.js LTS from Microsoft's winget (OpenJS.NodeJS.LTS)", first);
                case "git": return ("Git for Windows from winget (Git.Git)", first);
                case "uv": return ("uv from winget (astral-sh.uv)", first);
                case "claude": return ("Claude Code using Anthropic's official installer (claude.ai/install.ps1, installs to your user folder)", first);
                case "codex":
                    if (Which("npm") == null) first.Add("node");
                    return ("Codex CLI from npm (npm install -g @openai/codex)", first);
                default: return (null, first);
            }
        }

        public static async Task<bool> Install(string id, Action<string> log, CancellationToken ct = default)
        {
            RefreshPath();
            (string exe, string[] args) cmd = id switch
            {
                "node" => ("winget", Winget("OpenJS.NodeJS.LTS")),
                "git" => ("winget", Winget("Git.Git")),
                "uv" => ("winget", Winget("astral-sh.uv")),
                "claude" => ("powershell", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "irm https://claude.ai/install.ps1 | iex" }),
                "codex" => ("npm", new[] { "install", "-g", "@openai/codex" }),
                _ => (null, null),
            };
            if (cmd.exe == null) { log("Nothing to install for " + id); return false; }
            var exe = cmd.exe == "powershell" ? "powershell.exe" : Which(cmd.exe);
            if (exe == null) { log($"{cmd.exe} isn't available on this PC."); return false; }

            log($"> {cmd.exe} {string.Join(" ", cmd.args)}");
            var (code, _) = await Run(exe, cmd.args, 15 * 60_000, null, l => { if (!string.IsNullOrWhiteSpace(l) && !IsSpinner(l)) log(l.TrimEnd()); }, ct);
            RefreshPath();
            // winget returns non-zero for "already installed"; the rescan afterwards is the real verdict
            log(code == 0 ? "Done." : $"Finished with exit code {code}.");
            return code == 0;
        }

        static string[] Winget(string pkgId) => new[] { "install", "--id", pkgId, "-e", "--source", "winget", "--silent",
                                                         "--accept-source-agreements", "--accept-package-agreements" };

        // winget draws progress bars with \r and block characters; skip those lines in the log
        static bool IsSpinner(string l) { var t = l.Trim(); return t.Length < 3 || t.All(c => "-\\|/█▒░ ".Contains(c)) || t.Contains("██"); }

        // ------------------------------------------------------------------------------------- sign-in
        /// <summary>Opens the CLI's own sign-in in a console window (it opens the browser). Returns when that window closes.</summary>
        public static async Task Login(string id, string exe)
        {
            var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = false, CreateNoWindow = false };
            if (id == "claude") { psi.ArgumentList.Add("auth"); psi.ArgumentList.Add("login"); }
            else psi.ArgumentList.Add("login");
            psi.Environment["DISABLE_AUTOUPDATER"] = "1";
            using var p = Process.Start(psi);
            await p.WaitForExitAsync();
        }

        /// <summary>Codex stores an API key itself (codex login --with-api-key). Claude's goes into XenoStudio's Claude environment.</summary>
        public static async Task<string> SaveCodexKey(string exe, string key)
        {
            var (code, o) = await Run(exe, new[] { "login", "--with-api-key" }, 30000, key.Trim() + "\n");
            if (code != 0) throw new Exception(o.Trim().Split('\n').LastOrDefault() ?? "codex login failed");
            return "Saved";
        }

        public static string SetEnvLine(string env, string key, string value)
        {
            var lines = (env ?? "").Replace("\r", "").Split('\n')
                .Where(l => !l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) && l.Trim() != "").ToList();
            lines.Add($"{key}={value.Trim()}");
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------------------------- MCP catalog
        static string Cmd => Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

        public static List<McpCatalogEntry> Catalog(Settings s)
        {
            McpServer Npx(string name, string pkg) => new() { Name = name, Type = "stdio", Command = Cmd, Args = new() { "/c", "npx", "-y", pkg } };
            McpServer Uvx(string name, string pkg, params string[] more) => new() { Name = name, Type = "stdio", Command = Which("uvx") ?? "uvx", Args = new[] { pkg }.Concat(more).ToList() };
            var list = new List<McpCatalogEntry>
            {
                new() { Id = "desktop-commander", Name = "Desktop Commander", Needs = "node",
                        Desc = "Files, search, long-running terminal processes and edits across your drives.",
                        Make = () => Npx("desktop-commander", "@wonderwhy-er/desktop-commander@latest") },
                new() { Id = "windows-mcp", Name = "Windows-MCP", Needs = "uv",
                        Desc = "Lets the AIs see and drive Windows apps: screenshots, clicks, typing, windows.",
                        Make = () => Uvx("windows-mcp", "windows-mcp", "serve", "--transport", "stdio") },
                new() { Id = "playwright", Name = "Playwright browser", Needs = "node",
                        Desc = "A real browser the AIs can open, click through and read (testing web apps, looking things up).",
                        Make = () => Npx("playwright", "@playwright/mcp@latest") },
                new() { Id = "fetch", Name = "Fetch", Needs = "uv",
                        Desc = "Downloads a web page and turns it into readable text.",
                        Make = () => Uvx("fetch", "mcp-server-fetch") },
            };
            foreach (var e in list) e.Installed = (s.McpServers ?? new()).Any(m => m.Name.Equals(e.Id, StringComparison.OrdinalIgnoreCase) && m.Enabled);
            return list;
        }

        /// <summary>
        /// Starts the server and does the MCP handshake (initialize + tools/list), which also downloads the package the
        /// first time. Returns the number of tools it offers. Throws with the server's output if it doesn't answer.
        /// </summary>
        public static async Task<int> TestMcp(McpServer m, Action<string> log, int timeoutMs = 240_000)
        {
            var psi = new ProcessStartInfo { FileName = m.Command, UseShellExecute = false, CreateNoWindow = true,
                                             RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                                             StandardOutputEncoding = Encoding.UTF8 };
            foreach (var a in m.Args) psi.ArgumentList.Add(a);
            foreach (var kv in m.Env ?? new()) psi.Environment[kv.Key] = kv.Value;
            using var p = Process.Start(psi);
            var errTail = new Queue<string>();
            p.ErrorDataReceived += (_, e) => { if (string.IsNullOrWhiteSpace(e.Data)) return; lock (errTail) { errTail.Enqueue(e.Data); if (errTail.Count > 6) errTail.Dequeue(); } };
            p.BeginErrorReadLine();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                async Task Send(string json) { await p.StandardInput.WriteLineAsync(json); await p.StandardInput.FlushAsync(); }
                async Task<JsonNode> Wait(int id)
                {
                    while (true)
                    {
                        var line = await p.StandardOutput.ReadLineAsync(cts.Token) ?? throw new Exception("the server exited");
                        if (!line.TrimStart().StartsWith("{")) continue;
                        JsonNode j; try { j = JsonNode.Parse(line); } catch { continue; }
                        if ((int?)j?["id"] == id) return j;
                    }
                }
                log("Starting " + m.Name + " (the first run downloads it, this can take a minute)…");
                await Send("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"XenoStudio-setup\",\"version\":\"1\"}}}");
                var init = await Wait(1);
                if (init["error"] != null) throw new Exception((string)init["error"]["message"] ?? "initialize failed");
                log("Connected to " + ((string)init["result"]?["serverInfo"]?["name"] ?? m.Name) + ".");
                await Send("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                await Send("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
                var tools = (await Wait(2))["result"]?["tools"] as JsonArray;
                return tools?.Count ?? 0;
            }
            catch (Exception ex)
            {
                string tail; lock (errTail) tail = string.Join("\n", errTail);
                var why = ex is OperationCanceledException ? "it didn't answer in time" : ex.Message;
                throw new Exception(why + (tail != "" ? "\n" + tail : ""));
            }
            finally { try { p.Kill(true); } catch { } }
        }
    }
}
