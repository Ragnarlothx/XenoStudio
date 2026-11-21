using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace XenoStudio
{
    public sealed class ModelChoice
    {
        public string Id { get; set; } = "";              // "" = let the CLI pick its default
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Efforts { get; set; } = new();  // levels this model accepts
        public string DefaultEffort { get; set; } = "";
    }

    /// <summary>
    /// The models each CLI can use, for the model/effort pickers. Codex's list comes from the installed CLI itself
    /// (`codex debug models`), so new models appear without updating XenoStudio; Claude's from the current model family.
    /// </summary>
    public static class ModelCatalog
    {
        static (DateTime at, string exe, List<ModelChoice> list)? codexCache;

        public static readonly string[] ClaudeEfforts = { "low", "medium", "high", "xhigh", "max" };

        public static List<ModelChoice> Claude() => new()
        {
            new() { Id = "", Name = "Default", Description = "Whatever Claude Code picks for your plan", Efforts = ClaudeEfforts.ToList() },
            new() { Id = "claude-opus-5-5", Name = "Opus 5.5", Description = "Most capable for complex coding and reasoning", Efforts = ClaudeEfforts.ToList() },
            new() { Id = "claude-fable-5-2", Name = "Fable 5.2 (needs usage credits)", Description = "Large, careful model. Your plan needs usage credits for it; otherwise Claude replies with an error", Efforts = ClaudeEfforts.ToList() },
            new() { Id = "claude-sonnet-5-5", Name = "Sonnet 5.5", Description = "Fast and capable for everyday work", Efforts = ClaudeEfforts.ToList() },
            new() { Id = "claude-haiku-5-5", Name = "Haiku 5.5", Description = "Fastest, lightest", Efforts = ClaudeEfforts.ToList() },
        };

        public static async Task<List<ModelChoice>> Codex(string exe)
        {
            if (codexCache is { } c && c.exe == exe && DateTime.UtcNow - c.at < TimeSpan.FromMinutes(10)) return c.list;
            var list = new List<ModelChoice> { new() { Id = "", Name = "Default", Description = "Whatever Codex picks (your config.toml or its default)", Efforts = new() { "low", "medium", "high", "xhigh", "max", "ultra" } } };
            try
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add("debug"); psi.ArgumentList.Add("models");
                using var p = Process.Start(psi)!;
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (await Task.WhenAny(outTask, Task.Delay(15000)) != outTask) { try { p.Kill(true); } catch { } throw new TimeoutException(); }
                var models = JsonNode.Parse(await outTask)?["models"] as JsonArray ?? throw new InvalidDataException();
                foreach (var m in models)
                {
                    if ((string?)m?["visibility"] is "hide" or "hidden") continue;
                    var efforts = (m!["supported_reasoning_levels"] as JsonArray)?.Select(e => (string?)e?["effort"] ?? "").Where(e => e != "").ToList() ?? new();
                    list.Add(new ModelChoice
                    {
                        Id = (string?)m["slug"] ?? "", Name = (string?)m["display_name"] ?? (string?)m["slug"] ?? "",
                        Description = (string?)m["description"] ?? "", Efforts = efforts, DefaultEffort = (string?)m["default_reasoning_level"] ?? "",
                    });
                }
            }
            catch
            {
                // CLI too old / offline: offer the common ones so the picker still works
                foreach (var (id, name) in new[] { ("gpt-6.2-astra", "GPT-6.2-Astra"), ("gpt-6.2-sol", "GPT-6.2-Sol"), ("gpt-6-astra", "GPT-6-Astra"), ("gpt-6-sol", "GPT-6-Sol") })
                    list.Add(new ModelChoice { Id = id, Name = name, Efforts = new() { "low", "medium", "high", "xhigh", "max" } });
            }
            codexCache = (DateTime.UtcNow, exe, list);
            return list;
        }
    }
}
