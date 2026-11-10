XenoStudio v1.2 — Claude + Codex in one chat window (forked from XenoDuo; XenoDuo is untouched)
=============================================================================================

RUN:      dist\XenoStudio.exe
BUILD:    dotnet build -c Release -o dist      (C# .NET 8 WPF + WebView2)
PACKAGE:  powershell -ExecutionPolicy Bypass -File _dev\package.ps1 [-NoRuntime]   -> release\XenoStudio-v<ver>.zip
CLI:      XenoStudio.exe --send "message"      starts a new chat with that message

PORTABLE
 Everything lives next to the exe:
   runtime\claude.exe, runtime\codex.exe   pinned CLIs. Never auto-updated; Settings -> "Update bundled CLIs" copies
                                           your installed versions in when YOU choose.
   data\settings.json                      settings (first run copies XenoDuo's settings as the starting point)
   data\chats\                             chat transcripts + session ids
   data\profiles\claude-settings.json      optional Claude Code settings file, passed with --settings
   data\mcp-claude.json                    generated from the MCP list each turn
 The package script never includes data\. Sign-in stays in each user's own ~/.claude and ~/.codex.
 Receivers need the .NET 8 Desktop Runtime and WebView2 (built into Windows 11), then sign in once:
   runtime\claude.exe  (then /login)      runtime\codex.exe login

MCP SERVERS
 One shared list in Settings -> MCP. Each server: stdio (command + args + env) or http/sse (url + headers),
 enabled, and whether Claude and/or Codex get it.
   Claude: written to data\mcp-claude.json, passed with --mcp-config. "Strict" ignores ~/.claude.json servers.
           "Auto-allow" pre-approves mcp__<name> tools so they work even in read-only mode.
   Codex:  passed as -c mcp_servers.<name>.* overrides (works with or without ~/.codex/config.toml).
 "Import" pulls servers from ~/.claude.json and ~/.codex/config.toml.

COMMANDS IN CHAT
 !<command>   runs in the working folder with PowerShell / pwsh / cmd (Settings). Output is shown to the AIs
              on their next turn unless that is turned off.
 @claude / @codex / @both   address someone. AIs can hand off with @mentions.

SETTINGS (new vs XenoDuo)
 General: your name, extra instructions for both, turn time limit, auto-export, shell on/off/which shell.
 Claude:  on/off, bundled vs installed exe, model, fallback model, max agent steps, allowed/disallowed tools,
          extra dirs, own instructions, extra CLI args, env vars, strict MCP, auto-allow MCP.
 Codex:   on/off, bundled vs installed exe, model, reasoning effort, sandbox, load config.toml,
          own instructions, extra CLI args, env vars.
 UI prefs are stored in settings.ui (owned by web\app.js).

SOURCE
 Settings.cs (settings model, paths, MCP/TOML helpers), Agents.cs (CLI runners + JSONL parsing),
 MainWindow.xaml.cs (bridge, turn-taking, shell, persistence), web\ (UI).
