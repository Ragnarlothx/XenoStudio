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
    /// <summary>Result of one agent turn.</summary>
    public class AgentResult
    {
        public bool Ok;
        public string Text = "";
        public string Error;
        public string SessionId;
        public List<string> Tools = new();
    }

    /// <summary>
    /// One AI participant. Each turn runs the vendor's official CLI headless (signed in with Xeno's own plan),
    /// feeds the prompt on stdin, and parses its JSONL event stream.
    /// kind passed to onEvent: "delta" (text to append live) or "tool" (a tool/command the agent used).
    /// </summary>
    public abstract class Agent
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string ResolveExe(Settings s);
        /// <param name="images">absolute paths of pictures attached since this agent's last turn (may be empty)</param>
        public abstract Task<AgentResult> RunAsync(string prompt, string sessionId, string systemNote, Settings s,
                                                   IReadOnlyList<string> images, Action<string, string> onEvent, CancellationToken ct);

        /// <summary>Runs a process, writes stdin, streams stdout lines to onLine. Returns (exitCode, stderrTail).</summary>
        protected static async Task<(int code, string err)> RunProcess(string exe, IEnumerable<string> args, string cwd,
            string stdin, Action<string> onLine, CancellationToken ct, IDictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false),
                WorkingDirectory = Directory.Exists(cwd) ? cwd : Environment.CurrentDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["NO_COLOR"] = "1";
            if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

            using var p = new Process { StartInfo = psi };
            var err = new StringBuilder();
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (err) { err.AppendLine(e.Data); if (err.Length > 8000) err.Remove(0, err.Length - 8000); }
            };
            p.Start();
            p.BeginErrorReadLine();
            using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });

            // A CLI that rejects its arguments exits before reading stdin; writing then throws "The pipe is being
            // closed", which hid the CLI's own error message. Swallow that and report stderr instead.
            try
            {
                await p.StandardInput.WriteAsync(stdin ?? "");
                p.StandardInput.Close();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }

            string line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (line.Length == 0) continue;
                try { onLine(line); } catch { /* one bad line must not kill the turn */ }
            }
            await p.WaitForExitAsync();
            string tail; lock (err) tail = err.ToString().Trim();
            return (p.ExitCode, tail);
        }

        protected static string Short(string s, int n = 140)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        protected static string Str(JsonNode n) { try { return n?.GetValue<string>(); } catch { return n?.ToJsonString(); } }
    }

    // ------------------------------------------------------------------------------------------------
    public class ClaudeAgent : Agent
    {
        public override string Id => "claude";
        public override string Name => "Claude";

        // XenoStudio ships its own pinned claude.exe in runtime\ so updaters / path lookups on C:\ can't change it.
        // It is only replaced when the user clicks "Update bundled CLIs" (see UpdateBundled).
        public override string ResolveExe(Settings s)
        {
            var sp = (s.ClaudePath ?? "").Trim().Trim('"');
            if (sp != "" && Path.IsPathRooted(sp) && File.Exists(sp)) return sp;
            if (s.ClaudeUseBundled && File.Exists(Paths.BundledClaude)) return Paths.BundledClaude;
            return FindInstalled(s) ?? (File.Exists(Paths.BundledClaude) ? Paths.BundledClaude : "claude.exe");
        }

        public static string FindInstalled(Settings s)
        {
            var roamings = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) };
            var envRoam = Environment.GetEnvironmentVariable("APPDATA"); if (!string.IsNullOrEmpty(envRoam)) roamings.Add(envRoam);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
            roamings.Add(Path.Combine(home, "AppData", "Roaming"));

            var cands = new List<string>();
            var sp = (s.ClaudePath ?? "").Trim().Trim('"');
            if (sp != "")
            {
                if (Path.IsPathRooted(sp)) cands.Add(sp);
                else foreach (var r in roamings) cands.Add(Path.Combine(r, "npm", "node_modules", "@anthropic-ai", "claude-code", sp));
            }
            foreach (var r in roamings.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var pkg = Path.Combine(r, "npm", "node_modules", "@anthropic-ai", "claude-code");
                cands.Add(Path.Combine(pkg, "bin", "claude.exe"));
                cands.Add(Path.Combine(pkg, "node_modules", "@anthropic-ai", "claude-code-win32-x64", "claude.exe"));
            }
            cands.Add(Path.Combine(home, ".local", "bin", "claude.exe"));
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                try { cands.Add(Path.Combine(dir, "claude.exe")); } catch { }

            foreach (var c in cands) try { if (File.Exists(c) && !string.Equals(Path.GetFullPath(c), Paths.BundledClaude, StringComparison.OrdinalIgnoreCase)) return c; } catch { }
            return null;
        }

        public override async Task<AgentResult> RunAsync(string prompt, string sessionId, string systemNote, Settings s,
                                                         IReadOnlyList<string> images, Action<string, string> onEvent, CancellationToken ct)
        {
            // exe can vanish for a while when another Claude Code instance updates itself — re-resolve and retry for ~20s
            for (int attempt = 1; ; attempt++)
            {
                try { return await RunOnce(prompt, sessionId, systemNote, s, onEvent, ct); }
                catch (System.ComponentModel.Win32Exception ex) when (attempt < 6)
                {
                    onEvent?.Invoke("tool", $"claude.exe failed to start ({ex.NativeErrorCode}: {ex.Message}) — exe: {ResolveExe(s)} — cwd: {s.PrimaryWorkDir} — retrying ({attempt}/5)…");
                    await Task.Delay(4000, ct);
                }
            }
        }

        async Task<AgentResult> RunOnce(string prompt, string sessionId, string systemNote, Settings s,
                                        Action<string, string> onEvent, CancellationToken ct)
        {
            var r = new AgentResult();
            var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages" };
            foreach (var d in s.ExtraDirs(includeClaudeExtras: true)) { args.Add("--add-dir"); args.Add(d); }
            // attached pictures are referenced by path in the prompt; Claude opens them with its Read tool
            if (Directory.Exists(Paths.Uploads)) { args.Add("--add-dir"); args.Add(Paths.Uploads); }
            if (string.IsNullOrEmpty(sessionId)) { r.SessionId = Guid.NewGuid().ToString(); args.Add("--session-id"); args.Add(r.SessionId); }
            else { r.SessionId = sessionId; args.Add("--resume"); args.Add(sessionId); }
            if (!string.IsNullOrWhiteSpace(s.ClaudeModel)) { args.Add("--model"); args.Add(s.ClaudeModel.Trim()); }
            if (!string.IsNullOrWhiteSpace(s.ClaudeFallbackModel)) { args.Add("--fallback-model"); args.Add(s.ClaudeFallbackModel.Trim()); }
            if (s.ClaudeMaxTurns > 0) { args.Add("--max-turns"); args.Add(s.ClaudeMaxTurns.ToString()); }

            var allowed = new List<string>();
            switch (s.ClaudeMode)
            {
                case "full": args.Add("--permission-mode"); args.Add("bypassPermissions"); break;
                case "edit": args.Add("--permission-mode"); args.Add("acceptEdits"); break;
                default:
                    args.Add("--permission-mode"); args.Add("default");
                    allowed.AddRange(new[] { "Read", "Glob", "Grep", "WebSearch", "WebFetch" });
                    break;
            }

            var mcp = (s.McpServers ?? new()).Where(m => m.Enabled && m.Claude).ToList();
            if (mcp.Count > 0)
            {
                Directory.CreateDirectory(Paths.Data);
                File.WriteAllText(Paths.ClaudeMcpFile, Util.ClaudeMcpJson(mcp), new UTF8Encoding(false));
                args.Add("--mcp-config"); args.Add(Paths.ClaudeMcpFile);
                if (s.ClaudeAutoAllowMcp) allowed.AddRange(mcp.Select(m => "mcp__" + m.Name));
            }
            if (s.ClaudeStrictMcp) args.Add("--strict-mcp-config");
            if (File.Exists(Paths.ClaudeSettingsProfile)) { args.Add("--settings"); args.Add(Paths.ClaudeSettingsProfile); }

            allowed.AddRange((s.ClaudeAllowedTools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (allowed.Count > 0) { args.Add("--allowedTools"); args.Add(string.Join(",", allowed.Distinct())); }
            if (!string.IsNullOrWhiteSpace(s.ClaudeDisallowedTools)) { args.Add("--disallowedTools"); args.Add(s.ClaudeDisallowedTools.Trim()); }

            args.Add("--append-system-prompt"); args.Add(systemNote);
            args.AddRange(Util.SplitArgs(s.ClaudeExtraArgs));

            // Claude Code auto-updates by swapping its own exe; keep headless runs from doing that mid-chat.
            var env = Util.ParseEnv(s.ClaudeEnv);
            env["DISABLE_AUTOUPDATER"] = "1";

            var texts = new List<string>();
            var live = new StringBuilder();
            string resultText = null, resultErr = null;
            bool sawBlock = false;

            var (code, err) = await RunProcess(ResolveExe(s), args, s.PrimaryWorkDir, prompt, line =>
            {
                var j = JsonNode.Parse(line);
                switch (Str(j?["type"]))
                {
                    case "system":
                        var sid = Str(j["session_id"]);
                        if (!string.IsNullOrEmpty(sid)) r.SessionId = sid;
                        break;
                    case "stream_event":
                        var ev = j["event"];
                        var et = Str(ev?["type"]);
                        if (et == "content_block_start" && Str(ev["content_block"]?["type"]) == "text")
                        {
                            if (sawBlock && live.Length > 0) { live.Append("\n\n"); onEvent("delta", "\n\n"); }
                            sawBlock = true;
                        }
                        else if (et == "content_block_delta" && Str(ev["delta"]?["type"]) == "text_delta")
                        {
                            var t = Str(ev["delta"]["text"]) ?? "";
                            live.Append(t); onEvent("delta", t);
                        }
                        break;
                    case "assistant":
                        if (j["message"]?["content"] is JsonArray arr)
                            foreach (var c in arr)
                            {
                                var ct2 = Str(c?["type"]);
                                if (ct2 == "text") { var t = Str(c["text"]); if (!string.IsNullOrWhiteSpace(t)) texts.Add(t.Trim()); }
                                else if (ct2 == "tool_use")
                                {
                                    var name = Str(c["name"]) ?? "tool";
                                    var inp = c["input"];
                                    string detail = Str(inp?["command"]) ?? Str(inp?["file_path"]) ?? Str(inp?["pattern"])
                                                    ?? Str(inp?["url"]) ?? Str(inp?["query"]) ?? "";
                                    var tool = (name + " " + Short(detail, 120)).Trim();
                                    r.Tools.Add(tool); onEvent("tool", tool);
                                    // the tool_use arrives before the tool runs: the host snapshots the file as it was
                                    if (name is "Edit" or "Write" or "MultiEdit" or "NotebookEdit" && Str(inp?["file_path"] ?? inp?["notebook_path"]) is { Length: > 0 } fp)
                                        onEvent("file", fp);
                                }
                            }
                        break;
                    case "result":
                        var rsid = Str(j["session_id"]);
                        if (!string.IsNullOrEmpty(rsid)) r.SessionId = rsid;
                        bool isErr = j["is_error"]?.GetValue<bool>() == true || (Str(j["subtype"]) ?? "success") != "success";
                        if (isErr) resultErr = Str(j["result"]) ?? Str(j["subtype"]) ?? "error";
                        else resultText = Str(j["result"]);
                        break;
                }
            }, ct, env);

            if (ct.IsCancellationRequested) { r.Error = "Stopped."; r.Text = live.ToString(); return r; }
            if (resultErr != null) texts.RemoveAll(t => t == resultErr.Trim());   // CLI echoes its error as a synthetic assistant message
            r.Text = texts.Count > 0 ? string.Join("\n\n", texts) : (resultErr != null ? "" : (resultText ?? live.ToString()));
            if (resultErr != null || (code != 0 && string.IsNullOrWhiteSpace(r.Text)))
            {
                r.Error = resultErr ?? (string.IsNullOrWhiteSpace(err) ? $"claude exited with code {code}" : Short(err, 1200));
                return r;
            }
            r.Ok = true;
            return r;
        }
    }

    // ------------------------------------------------------------------------------------------------
    /// <summary>
    /// A backup seat (DeepSeek, Grok) over an OpenAI-compatible /chat/completions API (pay-as-you-go key, no subscription).
    /// Chat only: no file or shell access. The API is stateless, so XenoStudio keeps the history in data\sessions\&lt;id&gt;.json.
    /// Point the base URL at http://localhost:11434/v1 (Ollama) or LM Studio to run a free local model in the seat instead.
    /// </summary>
    public class ApiAgent : Agent
    {
        readonly string id, name;
        public ApiAgent(string id, string name) { this.id = id; this.name = name; }
        public override string Id => id;
        public override string Name => name;
        static readonly System.Net.Http.HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        public override string ResolveExe(Settings s) => Endpoint(s.Api(id));

        static string Endpoint(ApiSeat a)
        {
            var b = (a.BaseUrl ?? "").Trim().TrimEnd('/');
            return b.EndsWith("/chat/completions") ? b : b + "/chat/completions";
        }

        static string Key(ApiSeat a)
        {
            var k = (a.ApiKey ?? "").Trim();
            return k != "" ? k : (Environment.GetEnvironmentVariable(a.KeyEnv) ?? "").Trim();
        }

        public override async Task<AgentResult> RunAsync(string prompt, string sessionId, string systemNote, Settings s,
                                                         IReadOnlyList<string> images, Action<string, string> onEvent, CancellationToken ct)
        {
            // chat-only seats get the picture paths in the prompt text but can't open them
            var a = s.Api(id);
            var r = new AgentResult { SessionId = string.IsNullOrEmpty(sessionId) ? Guid.NewGuid().ToString("N") : sessionId };
            var file = Path.Combine(Paths.Sessions, r.SessionId + ".json");
            if (!string.IsNullOrEmpty(sessionId) && !File.Exists(file)) { r.Error = "session not found"; return r; }

            JsonArray history;
            try { history = File.Exists(file) ? (JsonNode.Parse(File.ReadAllText(file)) as JsonArray ?? new()) : new(); } catch { history = new(); }
            var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemNote } };
            foreach (var h in history) messages.Add(h?.DeepClone());
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });

            var body = new JsonObject { ["model"] = (a.Model ?? "").Trim(), ["messages"] = messages, ["stream"] = true };
            if (a.MaxTokens > 0) body["max_tokens"] = a.MaxTokens;

            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, Endpoint(a))
            { Content = new System.Net.Http.StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            var key = Key(a);
            if (key != "") req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            else if (Endpoint(a).Contains(a.KeyHost)) { r.Error = $"No {name} API key set."; return r; }

            var text = new StringBuilder();
            bool thinking = false;
            try
            {
                using var resp = await Http.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var eb = await resp.Content.ReadAsStringAsync(ct);
                    string em = null; try { em = Str(JsonNode.Parse(eb)?["error"]?["message"]); } catch { }
                    r.Error = $"HTTP {(int)resp.StatusCode}: {em ?? Short(eb, 600)}";
                    return r;
                }
                using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var rd = new StreamReader(stream, Encoding.UTF8);
                string line;
                while ((line = await rd.ReadLineAsync(ct)) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    var data = line.Substring(5).Trim();
                    if (data == "[DONE]") break;
                    JsonNode j; try { j = JsonNode.Parse(data); } catch { continue; }
                    var d = j?["choices"]?[0]?["delta"];
                    var rc = Str(d?["reasoning_content"]);
                    if (!string.IsNullOrEmpty(rc) && a.ShowReasoning && !thinking) { thinking = true; r.Tools.Add("thinking…"); onEvent("tool", "thinking…"); }
                    var c = Str(d?["content"]);
                    if (!string.IsNullOrEmpty(c)) { text.Append(c); onEvent("delta", c); }
                }
            }
            catch (OperationCanceledException) { r.Error = "Stopped."; r.Text = text.ToString(); return r; }
            catch (Exception ex) { r.Error = ex.Message; r.Text = text.ToString(); return r; }

            r.Text = text.ToString().Trim();
            if (r.Text.Length == 0) { r.Error = "empty reply"; return r; }

            history.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });
            history.Add(new JsonObject { ["role"] = "assistant", ["content"] = r.Text });
            try
            {
                Directory.CreateDirectory(Paths.Sessions);
                File.WriteAllText(file + ".tmp", history.ToJsonString());
                File.Move(file + ".tmp", file, true);
            }
            catch { }
            r.Ok = true;
            return r;
        }
    }

    // ------------------------------------------------------------------------------------------------
    public class CodexAgent : Agent
    {
        public override string Id => "codex";
        public override string Name => "Codex";

        public override string ResolveExe(Settings s)
        {
            var sp = (s.CodexPath ?? "").Trim().Trim('"');
            if (sp != "" && File.Exists(sp)) return sp;
            if (s.CodexUseBundled && File.Exists(Paths.BundledCodex)) return Paths.BundledCodex;
            return FindInstalled() ?? (File.Exists(Paths.BundledCodex) ? Paths.BundledCodex : "codex.exe");
        }

        public static string FindInstalled()
        {
            var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            try
            {
                var newest = Directory.Exists(bin)
                    ? new DirectoryInfo(bin).GetFiles("codex.exe", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                    : null;
                if (newest != null) return newest.FullName;
            }
            catch { }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                try { var c = Path.Combine(dir, "codex.exe"); if (File.Exists(c)) return c; } catch { }
            // npm install -g @openai/codex: the real exe sits inside the package (PATH only has a codex.cmd shim)
            try
            {
                var pkg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex");
                if (Directory.Exists(pkg))
                {
                    var exe = new DirectoryInfo(pkg).GetFiles("codex.exe", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                    if (exe != null) return exe.FullName;
                }
            }
            catch { }
            return null;
        }

        public override async Task<AgentResult> RunAsync(string prompt, string sessionId, string systemNote, Settings s,
                                                         IReadOnlyList<string> images, Action<string, string> onEvent, CancellationToken ct)
        {
            var r = new AgentResult { SessionId = sessionId };
            var sandbox = s.CodexSandbox is "workspace-write" or "danger-full-access" ? s.CodexSandbox : "read-only";
            var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "-s", sandbox, "-C", s.PrimaryWorkDir };
            foreach (var d in s.ExtraDirs(includeClaudeExtras: false)) { args.Add("--add-dir"); args.Add(d); }
            if (!s.CodexUseConfig)
            {
                args.Add("--ignore-user-config");
                // without config.toml the Windows sandbox isn't set up and every write is refused; use the same mode the app uses
                if (sandbox != "read-only" && OperatingSystem.IsWindows()) { args.Add("-c"); args.Add("windows.sandbox=\"elevated\""); }
            }
            if (!string.IsNullOrWhiteSpace(s.CodexModel)) { args.Add("-m"); args.Add(s.CodexModel.Trim()); }
            if (!string.IsNullOrWhiteSpace(s.CodexReasoning)) { args.Add("-c"); args.Add($"model_reasoning_effort=\"{s.CodexReasoning.Trim()}\""); }
            args.AddRange(Util.CodexMcpOverrides((s.McpServers ?? new()).Where(m => m.Enabled && m.Codex)));
            args.AddRange(Util.SplitArgs(s.CodexExtraArgs));
            // -i belongs to whichever subcommand runs: `exec` for a new thread, `exec resume` when continuing one
            var imageArgs = (images ?? Array.Empty<string>()).Where(File.Exists).SelectMany(i => new[] { "-i", i }).ToList();
            if (!string.IsNullOrEmpty(sessionId)) { args.Add("resume"); args.AddRange(imageArgs); args.Add(sessionId); }
            else args.AddRange(imageArgs);
            args.Add("-");

            // Codex has no per-call system prompt flag; the note rides at the top of the first turn and lives in the thread after that.
            var input = string.IsNullOrEmpty(sessionId) ? systemNote + "\n\n---\n\n" + prompt : prompt;

            var texts = new List<string>();
            string failMsg = null;

            var (code, err) = await RunProcess(ResolveExe(s), args, s.PrimaryWorkDir, input, line =>
            {
                if (!line.StartsWith("{")) return;
                var j = JsonNode.Parse(line);
                var type = Str(j?["type"]);
                switch (type)
                {
                    case "thread.started":
                        var tid = Str(j["thread_id"]);
                        if (!string.IsNullOrEmpty(tid)) r.SessionId = tid;
                        break;
                    case "item.started":
                    case "item.completed":
                        var it = j["item"];
                        var itype = Str(it?["type"]);
                        if (itype == "agent_message" && type == "item.completed")
                        {
                            var t = Str(it["text"]);
                            if (!string.IsNullOrWhiteSpace(t))
                            {
                                if (texts.Count > 0) onEvent("delta", "\n\n");
                                texts.Add(t.Trim()); onEvent("delta", t.Trim());
                            }
                        }
                        else if (type == "item.started" && itype == "command_execution")
                        { var tool = "$ " + Short(Str(it["command"]), 140); r.Tools.Add(tool); onEvent("tool", tool); }
                        else if (itype == "file_change")
                        {
                            var full = (it["changes"] as JsonArray)?.Select(c => Str(c?["path"]) ?? "").Where(x => x != "").ToList() ?? new();
                            // started = before the patch is applied (host snapshots); completed = after (host only learns the path)
                            foreach (var p in full) onEvent(type == "item.started" ? "file" : "fileDone", p);
                            if (type == "item.completed")
                            {
                                var tool = "edit " + string.Join(", ", full.Select(Path.GetFileName));
                                r.Tools.Add(tool.Trim()); onEvent("tool", tool.Trim());
                            }
                        }
                        else if (type == "item.started" && itype == "mcp_tool_call")
                        { var tool = $"{Str(it["server"])}.{Str(it["tool"])}"; r.Tools.Add(tool); onEvent("tool", tool); }
                        else if (type == "item.started" && itype == "web_search")
                        { var tool = "search " + Short(Str(it["query"]), 120); r.Tools.Add(tool); onEvent("tool", tool); }
                        else if (itype == "error" && type == "item.completed")
                        { failMsg ??= Str(it["message"]); }
                        break;
                    case "turn.failed":
                        failMsg = Str(j["error"]?["message"]) ?? failMsg ?? "turn failed";
                        break;
                    case "error":
                        failMsg = Str(j["message"]) ?? failMsg;
                        break;
                }
            }, ct, Util.ParseEnv(s.CodexEnv));

            r.Text = string.Join("\n\n", texts);
            if (ct.IsCancellationRequested) { r.Error = "Stopped."; return r; }
            if (texts.Count == 0 && (failMsg != null || code != 0))
            {
                r.Error = failMsg ?? (string.IsNullOrWhiteSpace(err) ? $"codex exited with code {code}" : Short(err, 1200));
                return r;
            }
            r.Ok = true;
            return r;
        }
    }
}
