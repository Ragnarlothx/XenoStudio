# XenoStudio

**Claude and Codex in one chat window, working on your files together, with a code workspace that shows what they change.**
A Windows desktop app by **Xeno** (C# .NET 8 WPF + WebView2).

**[Download the latest release](../../releases/latest)** → unzip → run `XenoStudio.exe`. A setup wizard installs anything missing and
signs you in with your own accounts. Step-by-step: **[INSTALL.md](INSTALL.md)**.

![XenoStudio chat](docs/screenshots/chat.png)

> Screenshots use a made-up demo project ("aurora-shop") and a demo chat. No real files or conversations.

---

## What it is

XenoStudio runs the official **Claude Code** and **Codex** command-line tools behind one chat. You talk to both;
they see each other's replies, can hand work to each other with `@mentions`, and can read, edit and run things in
your work folders. Everything they change shows up as a **change card** you can open as a before/after diff, and a
**code workspace** beside the chat lets you read, edit and save code without leaving the conversation.

- Uses your own Claude and ChatGPT plans (sign-in stays in your `~/.claude` and `~/.codex`); no API keys needed for them.
- Optional backup seats (DeepSeek, Grok, or a local model via Ollama / LM Studio) that answer when a main AI fails.
- Portable: everything lives next to the exe.

---

## 2-minute demo

A walk through one real-looking session. You can follow along on any small project.

**1. Ask for a change.** Type a normal message. With **Send to: Auto**, both enabled AIs answer in turn.

> *Add discount codes to the cart in src/cart.js: AURORA10 for 10% off, NORTHERN25 for 25%. @codex check the math after.*

**2. Watch the reply.** Claude's tool lines (`Read src/cart.js`, `Edit …\cart.js`) show what it's doing live. Long code in
a reply becomes a **code tile** (`snippet.js · javascript · 9 lines`) instead of a wall of text. And because Claude
edited a file, a **change card** appears under its reply: `claude changed …\src\cart.js +13 −2`.

**3. Hand-off.** The `@codex` in your message gives Codex a turn after Claude. It runs the demo, finds a rounding bug
(prices are in cents), fixes it, and gets its own change card: `codex changed …\cart.js +1 −1`.

**4. See exactly what changed.** Click a change card. The workspace opens with a **red/green diff** of that edit:

![Diff of an AI change](docs/screenshots/diff.png)

**5. Read or edit the file yourself.** Click any file name in the chat (or **Code workspace** in the top bar). The
workspace shows the file with full syntax highlighting. **Edit code** makes it editable; **Save file** writes it to
disk, and the chat gets a note (*edited `src\cart.js` in the editor (+1 −1)*) so both AIs know on their next turn.

![Code workspace](docs/screenshots/workspace.png)

**6. Send code back.** Select some lines and press **Attach code to chat**. They ride along with your next message as
context, without pasting.

**7. Live reload.** Keep a file open while an AI edits it: the workspace reloads, and asks first if you have unsaved changes.

---

## Features

### Chat
| | |
|---|---|
| **Two AIs, one thread** | Claude (Claude Code CLI) and Codex (Codex CLI) answer in turn and see each other's messages |
| **@mentions and hand-off** | `@claude`, `@codex`, `@both`, `@all`. AIs can pass work to each other the same way; a limit on AI replies in a row keeps loops in check |
| **Send to** | Auto, Claude + Codex, one AI, or All + backup |
| **Backup seats** | DeepSeek and Grok over any OpenAI-compatible API (also local Ollama / LM Studio). Can answer automatically when Claude or Codex hits a limit or fails |
| **Pictures** | Paste (Ctrl+V), drag in, or **+ Picture**. Codex receives the image itself; Claude opens it from disk |
| **Shell** | `!command` runs in the work folder (PowerShell, pwsh or cmd); the output is shared with the AIs |
| **Retry and recovery** | Failed turns retry once by default; lost CLI sessions restart with recent history |
| **Export** | Save any chat as Markdown |

### Code workspace
| | |
|---|---|
| **Code tiles** | Long code blocks in replies become compact tiles; click to open in the workspace |
| **Editor** | Monaco (the VS Code editor, from XenoEdit): highlighting, folding, minimap, follows the app theme |
| **File links** | File names in messages and tool lines open the file |
| **Edit and save** | Edit in place, **Save file** to disk, or **Save as…** for code blocks |
| **Attach code to chat** | Send a selection or the whole draft with your next message |
| **Open in XenoEdit** | Hands the file (or an unsaved block) to XenoEdit for full editing. Found automatically from a Desktop / Start Menu shortcut, or set the path in Settings |

### AI change tracking
| | |
|---|---|
| **Change cards** | Every file Claude or Codex edits during a reply is listed under that reply with lines added/removed |
| **Diffs** | Each card opens the before/after of that exact edit, stored with the chat |
| **How "before" is captured** | Claude reports an edit just before making it, so its diffs are exact. Codex reports edits after they land, so XenoStudio snapshots likely files before each turn (files the prompt mentions and files edited earlier in the chat); if Codex edits anything else, the card says *before not captured* and shows the result |
| **Limits** | Edits made through shell commands (a script writing a file) aren't reported by the CLIs, so they don't get cards |

### Look
Four themes: **Dark**, **Light**, **Cyberpunk** and **Hacker**. The Windows 11 title bar and window border follow the theme.

| Cyberpunk | Hacker |
|---|---|
| ![Cyberpunk theme](docs/screenshots/theme-cyberpunk.png) | ![Hacker theme](docs/screenshots/theme-hacker.png) |

### App
- **Workspace and drives**: work in any folder or drive on the PC (local, USB, mapped network). **Settings → General → Workspace and drives** lists the drives found (ones that aren't connected are greyed out), with **Add drive**, **Add working folder…** and **Refresh drives**. Several entries are allowed (`D:\Projects,E:\Work`): the first is where the AIs start, the rest are added for both.
- **MCP servers**: one shared list for both AIs (stdio or http/sse), importable from your Claude and Codex configs; stale paths are repaired automatically when the Codex app updates.
- **Single instance**: launching the exe again brings the open window forward. `XenoStudio.exe --send "message"` starts a chat.
- Window position remembered; crash log in `data\crash.log`.

---

## Install and run

1. Windows 10/11 x64. Download the zip from **Releases**, unzip anywhere, run `XenoStudio.exe`. .NET is included; WebView2 is built into Windows 11.
2. The **setup wizard** opens on first launch if anything is missing:
   - **Tools and AIs**: checks Node.js, Git, uv, the Claude Code CLI and the Codex CLI, and installs what's missing (official sources, after a Yes/No prompt).
   - **Sign in**: uses the sign-ins already on your PC. If you aren't signed in, **Sign in** opens Claude's or Codex's own browser login. Already signed in? The step is skipped, or click **Skip**.
   - **MCP servers** (optional): Desktop Commander, Windows-MCP, Playwright, Fetch; each is tested before it's added.
   Reopen it any time from **Settings**.
3. Open **Settings** to pick your work folder(s), permissions and theme.

No accounts, keys or sign-ins ship with XenoStudio. Everyone uses their own Claude and ChatGPT accounts.
Full guide with troubleshooting: [INSTALL.md](INSTALL.md).

### Build
```
dotnet build -c Release -o dist
powershell -ExecutionPolicy Bypass -File _dev\package.ps1 -NoRuntime   # self-contained zip into release\
```
The bundled CLIs (`runtime\*.exe`) aren't in this repo. Without them XenoStudio uses installed copies, and the setup wizard installs them if needed.

---

## Access levels and rules for the AIs

Claude and Codex are real agents: depending on the access you give them, they can read files, change files and run
commands on your PC **without asking first**. XenoStudio runs them headless, so there are no "Allow?" pop-ups
mid-turn; the access level you pick in Settings *is* the permission.

### Access levels

| Setting | Level | What the AI can do |
|---|---|---|
| **Settings → Claude → Permissions** | `read` | Read and search files. Anything that needs approval (edits, commands) is refused. |
| | `edit` | Also create and edit files. Shell commands still need to be allowed (Allowed tools). |
| | `full` | Everything, with no approval: edit or delete any file it can reach, run any command, install software, use the network and every MCP tool. |
| **Settings → Codex → Access** | `read-only` | Read files and run commands that don't change anything. |
| | `workspace-write` | Write inside your work folders only. |
| | `danger-full-access` | No sandbox: any file and any command, anywhere on the PC. |

Start with **read** / **read-only**, move to **edit** / **workspace-write** for coding, and use **full** /
**danger-full-access** only when you want the AIs to manage your system: installing tools, working across drives, running
builds and scripts. MCP servers you add (for example Windows-MCP, which can click and type in any app) give the AIs
even more reach, at whatever access level you chose.

### Why rules matter at full access

At full access nothing technical stops a mistake. An AI that misunderstands "clean up this folder" can delete the wrong
one; a script can overwrite files; two AIs handing work to each other can repeat a change. AIs follow written instructions
well, so the most important safety step is **telling them your rules**. XenoStudio gives them to the AIs in every chat:

- **Settings → General → Instructions for both assistants**: rules for both AIs.
- **Settings → Claude → Claude instructions** and **Settings → Codex → Codex instructions**: rules for one AI.
- **Settings → Claude → Blocked tools**: hard blocks Claude can't get around (for example `Bash(rm:*)`).

Example rules to start from (edit to fit your PC):

```text
- Ask me before deleting, moving or overwriting anything outside the current project.
- Never change Windows settings, the registry, drivers, security software or firewall rules.
- Back up a file or folder before a large or risky change, and tell me where the backup is.
- Don't install, uninstall or update software without asking first.
- Stay inside my work folders unless I name another path.
- Never read, print, copy or send passwords, API keys, tokens, browser data or sign-in files.
- Don't upload, publish or send anything to the internet unless I ask.
- Don't touch these folders: D:\Photos, E:\Backups
- When you finish, list every file you changed.
```

Good habits on top of rules:
- **Keep backups** of anything important (or use git), especially before handing the AIs a big job.
- **Watch the change cards** under each reply. Click one to see exactly what changed.
- Keep work folders to what the job needs. At full access the AIs can reach further, but the work folders tell them where to work.
- **Stop** ends a turn at once if something looks wrong.

---

## Your data

Everything XenoStudio writes stays next to the exe in `data\`: settings, chats, attached pictures, edit snapshots, the
WebView2 profile. It's never committed or packaged. API keys are never exported. Deleting a chat also deletes its
pictures and snapshots.

## Project layout

| Path | Contents |
|---|---|
| `Agents.cs` | Runs Claude Code / Codex / API seats and parses their event streams |
| `MainWindow.xaml.cs` | Bridge to the UI, turn-taking, prompts, shell, persistence |
| `EditTracker.cs` | Snapshots and diffs of files the AIs change |
| `CodePreview.cs` | Workspace file access, Save as, Open in XenoEdit |
| `Setup.cs` | Setup wizard backend: tool/CLI/sign-in checks, prompted installs, MCP catalog |
| `Settings.cs` | Settings model, paths, MCP and work-folder helpers |
| `ThemeChrome.cs`, `App.xaml.cs` | Title bar colors, single instance, crash log |
| `web/` | The UI (`app.js` chat, `preview.js` workspace, `settings.js`, `style.css`), Monaco in `web/lib/vs` |
| `_dev/` | Packaging, signing and change-notes scripts |

Third-party: see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Full history: *XenoStudio Change Notes.pdf*.
