"""Builds 'XenoStudio Change Notes.pdf' (every change since the fork from XenoDuo).

Run with any Python that has reportlab:  python _dev\\make_changelog.py
"""
import os
import sys

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import inch
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (KeepTogether, PageBreak, Paragraph, SimpleDocTemplate, Spacer, Table,
                                TableStyle)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "XenoStudio Change Notes.pdf")

FONTS = r"C:\Windows\Fonts"
pdfmetrics.registerFont(TTFont("Segoe", os.path.join(FONTS, "segoeui.ttf")))
pdfmetrics.registerFont(TTFont("Segoe-Bold", os.path.join(FONTS, "segoeuib.ttf")))
pdfmetrics.registerFont(TTFont("Mono", os.path.join(FONTS, "consola.ttf")))
pdfmetrics.registerFontFamily("Segoe", normal="Segoe", bold="Segoe-Bold", italic="Segoe", boldItalic="Segoe-Bold")

ACCENT = colors.HexColor("#4f9cf9")
DARK = colors.HexColor("#1e1f22")
GREY = colors.HexColor("#6d7178")
LINE = colors.HexColor("#d9dce1")
CLAUDE = colors.HexColor("#c15f3c")
CODEX = colors.HexColor("#1f9d63")

s_title = ParagraphStyle("t", fontName="Segoe-Bold", fontSize=26, leading=30, textColor=DARK)
s_sub = ParagraphStyle("s", fontName="Segoe", fontSize=11, leading=15, textColor=GREY)
s_h1 = ParagraphStyle("h1", fontName="Segoe-Bold", fontSize=17, leading=22, textColor=ACCENT, spaceBefore=14, spaceAfter=4, keepWithNext=1)
s_h2 = ParagraphStyle("h2", fontName="Segoe-Bold", fontSize=12, leading=16, textColor=DARK, spaceBefore=10, spaceAfter=3, keepWithNext=1)
s_body = ParagraphStyle("b", fontName="Segoe", fontSize=9.5, leading=13.5, textColor=DARK, alignment=TA_LEFT)
s_meta = ParagraphStyle("m", parent=s_body, textColor=GREY, fontSize=9)
s_bullet = ParagraphStyle("li", parent=s_body, leftIndent=14, bulletIndent=3, spaceAfter=2)
s_cell = ParagraphStyle("c", parent=s_body, fontSize=8.8, leading=12)
s_cellb = ParagraphStyle("cb", parent=s_cell, fontName="Segoe-Bold")


def code(t):
    return f'<font name="Mono" size="8.6">{t}</font>'


def P(t, st=s_body):
    return Paragraph(t, st)


def bullets(items):
    return [Paragraph(i, s_bullet, bulletText="•") for i in items]


def table(rows, widths, header=True):
    data = [[Paragraph(str(c), s_cellb if (header and r == 0) else s_cell) for c in row] for r, row in enumerate(rows)]
    t = Table(data, colWidths=widths, repeatRows=1 if header else 0)
    style = [
        ("GRID", (0, 0), (-1, -1), 0.4, LINE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 5), ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 3), ("BOTTOMPADDING", (0, 0), (-1, -1), 3),
    ]
    if header:
        style.append(("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#eef4fe")))
    t.setStyle(TableStyle(style))
    return t


def by(who):
    color = CLAUDE if who == "Claude" else CODEX if who == "Codex" else GREY
    return f'<font color="#{color.hexval()[2:]}"><b>{who}</b></font>'


def footer(canvas, doc):
    canvas.saveState()
    canvas.setFont("Segoe", 8)
    canvas.setFillColor(GREY)
    canvas.drawString(0.75 * inch, 0.5 * inch, "XenoStudio Change Notes · v1.0.0 → v1.5.7")
    canvas.drawRightString(letter[0] - 0.75 * inch, 0.5 * inch, f"Page {doc.page}")
    canvas.setStrokeColor(LINE)
    canvas.line(0.75 * inch, 0.65 * inch, letter[0] - 0.75 * inch, 0.65 * inch)
    canvas.restoreState()


W = letter[0] - 1.5 * inch
story = []

# ---------------------------------------------------------------- cover / summary
story += [
    P("XenoStudio", s_title),
    P("Change Notes: every change from the fork to today", s_sub),
    Spacer(1, 4),
    P("Covers v1.0.0 (2026-11-01) through v1.5.7 (2026-11-12)", s_meta),
    Spacer(1, 10),
    P("Version summary", s_h1),
    table([
        ["Version", "Date", "By", "Headline"],
        ["1.0.0", "2026-11-01", "Claude + Codex", "Forked from XenoDuo into a separate, portable app: pinned CLIs, shared MCP list, shell commands, "
                                                   "DeepSeek/Grok backup seats, prompt templates, much larger settings."],
        ["1.1.0", "2026-11-09", "Claude", "Fixed Codex crashing (\"The pipe is being closed\"), multiple working folders (two drives), "
                                          "picture attachments, updated bundled Claude Code and Codex CLIs, text-encoding fixes."],
        ["1.2.0", "2026-11-09", "Claude + Codex", "Single instance (no duplicate windows), automatic repair of stale MCP paths, "
                                                  "crash-proof turns, retry once by default, remembered window position, crash log, "
                                                  "drag-and-drop overlay and fixes."],
        ["1.3.0", "2026-11-10", "Claude + Codex", "Cyberpunk and Hacker themes; the Windows 11 title bar and border follow the theme."],
        ["1.4.0", "2026-11-11", "Claude + Codex", "Code workspace with the Monaco editor (from XenoEdit), code tiles, change cards and "
                                                  "before/after diffs of every AI edit, live reload, Open in XenoEdit, safer updater, "
                                                  "README with demo, private GitHub repo and release."],
        ["1.5.0", "2026-11-12", "Claude + Codex", "Setup wizard: finds each user's own Claude/Codex sign-ins, offers browser sign-in, "
                                                  "installs missing tools after a prompt, one-click tested MCP servers; .NET included in the download."],
        ["1.5.1", "2026-11-12", "Claude", "Code review fixes for the setup wizard, new INSTALL.md guide, download link in the README."],
        ["1.5.2", "2026-11-12", "Claude", "Skip option for sign-in: automatic when already signed in, or per account."],
        ["1.5.3", "2026-11-12", "Codex", "Workspace and drives: pick any drive or folder on the PC, including USB and network drives."],
        ["1.5.4", "2026-11-12", "Claude", "Review fixes: no freeze from offline network drives, docs cleaned up."],
        ["1.5.6", "2026-11-12", "Codex", "Clear the previous script immediately and stop its watcher when switching files or diffs."],
        ["1.5.7", "2026-11-12", "Claude", "Update XenoStudio from GitHub: Settings → Runtime, checksum-verified, closes and reopens by itself."],
        ["1.5.5", "2026-11-12", "Claude", "Code workspace always switches to the clicked file, with a clear message when it can't be opened."],
    ], [0.6 * inch, 0.85 * inch, 1.05 * inch, W - 2.5 * inch]),
    Spacer(1, 8),
    P("Who did what: " + by("Claude") + " owns the C# backend (Settings.cs, Agents.cs, App/MainWindow) and did the web UI changes in 1.1.0 "
      "while Codex was down. " + by("Codex") + " owns the web UI (web\\) and did the 1.2.0 drag-and-drop work, the 1.3.0 theme styles and the 1.4.0 workspace UI.", s_meta),
]

# ---------------------------------------------------------------- 1.0.0
story += [
    P("v1.0.0: the fork from XenoDuo · 2026-11-01", s_h1),
    P("XenoStudio was created as a copy of XenoDuo so new ideas could be tried without touching the stable app. "
      "XenoDuo itself was never edited. Everything below is what 1.0.0 added on top of XenoDuo."),
    P("Portable layout", s_h2),
    *bullets([
        "Everything lives next to the exe: " + code("runtime\\") + " (pinned " + code("claude.exe") + " and " + code("codex.exe") + "), "
        + code("data\\settings.json") + ", " + code("data\\chats\\") + ", " + code("data\\profiles\\claude-settings.json") + ".",
        "Pinned CLIs are never auto-updated; Settings → \"Update bundled CLIs\" copies installed versions in only when you choose.",
        "First run copies XenoDuo's settings as a starting point (read-only; XenoDuo's own files are untouched).",
        "Packaging script " + code("_dev\\package.ps1") + " builds a shareable zip (with or without the CLIs) and never includes " + code("data\\") + ".",
        "Command line: " + code("XenoStudio.exe --send \"message\"") + " starts a new chat with that message.",
    ]),
    P("MCP servers", s_h2),
    *bullets([
        "One shared server list (stdio or http/sse), each switchable on/off and per AI (Claude and/or Codex).",
        "Claude gets them through a generated " + code("data\\mcp-claude.json") + " (" + code("--mcp-config") + "), with optional "
        "\"strict\" mode and auto-allow of the servers' tools.",
        "Codex gets them as " + code("-c mcp_servers.&lt;name&gt;.*") + " overrides, so they work with or without " + code("~/.codex/config.toml") + ".",
        "\"Import\" pulls servers from " + code("~/.claude.json") + " and " + code("~/.codex/config.toml") + ".",
    ]),
    P("Chat", s_h2),
    *bullets([
        code("!command") + " runs a shell command (PowerShell, pwsh or cmd) in the working folder; output is shared with the AIs unless turned off.",
        code("@claude") + " / " + code("@codex") + " / " + code("@both") + " addressing, and AI-to-AI hand-off by @mention.",
        "DeepSeek and Grok backup seats over any OpenAI-compatible API (also local Ollama / LM Studio), with optional automatic "
        "fallback when Claude or Codex fails.",
        "Editable prompt template and footer with {placeholders}, plus a preview of exactly what each AI receives.",
        "Lost-session recovery: if a CLI session disappears, a fresh one starts with the recent history.",
    ]),
    P("Settings added versus XenoDuo", s_h2),
    *bullets([
        "<b>General:</b> your name, extra instructions for both AIs, turn time limit, auto-export, shell on/off and which shell.",
        "<b>Claude:</b> on/off, bundled vs installed exe, model, fallback model, max agent steps, allowed/blocked tools, extra folders, "
        "own instructions, extra CLI args, environment variables, strict MCP, auto-allow MCP.",
        "<b>Codex:</b> on/off, bundled vs installed exe, model, reasoning effort, sandbox, load config.toml, own instructions, "
        "extra CLI args, environment variables.",
        "UI preferences (font size, accent, density, timestamps…) stored in " + code("settings.ui") + ".",
    ]),
    PageBreak(),
]

# ---------------------------------------------------------------- 1.1.0
story += [
    P("v1.1.0 · 2026-11-09 · " + by("Claude"), s_h1),
    P("Bug fixes", s_h2),
    table([
        ["Problem", "Cause", "Fix"],
        ["Codex failed every turn with \"The pipe is being closed\".",
         "The working folder was set to " + code("D:\\,E:\\") + ". It was passed to Codex as one folder path (" + code("-C \"D:\\,E:\\\"") + "), "
         "which Windows rejects (os error 123). Codex quit instantly, and XenoStudio crashed writing to it.",
         "Working folders are now a list (see New features). Reproduced and verified with the real CLI."],
        ["Real CLI errors were hidden.",
         "When a CLI exited before reading its input, the write threw \"The pipe is being closed\" and masked the CLI's own message.",
         "That write error is now ignored, so the CLI's actual error is shown."],
        ["Claude's extra folders were silently ignored.",
         "That setting only understood " + code(";") + ". " + code("D:\\,E:\\") + " was treated as one non-existent folder.",
         "Accepts " + code(",") + ", " + code(";") + " or " + code("|") + "."],
        ["Garbled characters in the UI (âœ•, Browseâ€¦, \"Version 1.0 ? Data\").",
         "Text-encoding damage in " + code("index.html") + " and " + code("app.js") + ".",
         "Restored ×, Browse… and the · separator."],
    ], [1.6 * inch, 2.6 * inch, W - 4.2 * inch]),
    P("New: multiple working folders", s_h2),
    *bullets([
        "The shared working folder can list several folders: " + code("D:\\,E:\\") + ". The first existing one is where the AIs start; "
        "the rest are added with " + code("--add-dir") + " for both Claude and Codex.",
        "Shell commands, export, the Folder button and settings export use the first folder.",
        "Settings → General: \"Add working folder…\" now <b>adds</b> the picked folder to the list instead of replacing it; "
        "\"Clear folders\" empties it.",
    ]),
    P("New: picture attachments", s_h2),
    *bullets([
        "Attach by pasting (Ctrl+V, including Win+Shift+S screenshots), dragging files onto the window, or the <b>+ Picture</b> button.",
        "Thumbnails above the message box with × to remove; pictures can be sent with or without text.",
        "Pictures show in the chat; click one to open it full size.",
        "Stored in " + code("data\\uploads\\&lt;chat&gt;\\") + " and deleted together with the chat. PNG, JPG, GIF, WebP, BMP; max 20 MB each.",
        "<b>Codex</b> receives the actual image (" + code("-i") + "; tested: it correctly answered \"Red\" for a red test square). "
        "<b>Claude</b> gets the file path and opens it with its Read tool. DeepSeek/Grok get the path only (no file access).",
        "Markdown export includes the pictures.",
    ]),
    P("Updates", s_h2),
    table([
        ["Component", "Before", "After", "Notes"],
        ["Claude Code (bundled)", "2.1.312", "2.1.321", "Official npm package, signature verified: Anthropic, PBC."],
        ["Codex CLI (bundled)", "0.166.0-alpha.5.1", "0.167.0-alpha.3.2", "Copied with its helper exes from the installed Codex app."],
        ["XenoStudio", "1.0.0", "1.1.0", ""],
    ], [1.5 * inch, 1.2 * inch, 1.2 * inch, W - 3.9 * inch]),
    P("Tooling", s_h2),
    *bullets([
        code("_update\\apply-update.ps1") + ": installs a new build after XenoStudio closes (the app can't replace itself while running), "
        "backs up the old app files, keeps " + code("data\\") + " untouched, then reopens the app.",
        "Old CLIs kept in " + code("_update\\old-runtime\\") + ".",
    ]),
]

# ---------------------------------------------------------------- 1.2.0
story += [
    P("v1.2.0 · 2026-11-09 · " + by("Claude") + " + " + by("Codex"), s_h1),
    P("A full backup of the app as it was (1.1.0, including source, build, chats and settings) was made first: "
      + code("Backups\\XenoStudio-v1.1.0-20261109-175842") + " (1.9 GB).", s_meta),
    P("Changes by " + by("Claude"), s_h2),
    table([
        ["#", "Change", "Details", "Verified"],
        ["1", "Only one XenoStudio at a time",
         "Launching the exe again brings the open window to the front instead of opening a second app. "
         + code("--send \"…\"") + " from a second launch is handed to the open window. Per install folder, so XenoDuo or a test copy can still run.",
         "Yes: second launch exited, one window"],
        ["2", "Stale MCP paths repaired automatically",
         "The node_repl server pointed at a Codex install removed by a Codex app update. At startup, any server whose command or "
         "environment paths no longer exist is re-read from the Codex/Claude config, keeping your on/off choices. A notice says which.",
         "Yes: refreshed to the current install"],
        ["3", "One AI crashing doesn't stop the others",
         "A crash inside a turn used to end the whole queue with a system error. It now becomes that AI's turn error, so retry, "
         "backup fallback and the next AI still run.",
         "Builds; only triggers on a real crash"],
        ["4", "Retry once by default",
         "Retry on error changed from 0 to 1 (5 s delay) to catch network blips and brief overloads. Applied once to existing "
         "settings through a new settings-version upgrade; still adjustable in Settings.",
         "Yes"],
        ["5", "Window position and size remembered",
         "Saved to " + code("data\\window.json") + " on close, including maximized. Falls back to the default if that screen is gone.",
         "Yes"],
        ["6", "Crash log",
         "Unexpected errors are written to " + code("data\\crash.log") + " (capped at 1 MB) with a popup, instead of the app silently dying.",
         "Builds; only triggers on a real crash"],
        ["7", "Version in the title bar", "\"XenoStudio 1.2.0\".", "Yes"],
        ["8", "Build cleanup", "Test files in " + code("_update\\") + " are excluded from compilation.", "Yes"],
    ], [0.25 * inch, 1.45 * inch, W - 3.25 * inch, 1.55 * inch]),
    P("Changes by " + by("Codex") + ": drag-and-drop", s_h2),
    *bullets([
        "Full-window drop overlay: \"Drop pictures to attach to your message\".",
        "Pictures whose Windows file type comes through blank are recognized by their extension (.png, .jpg, .jpeg, .gif, .webp, .bmp).",
        "A message appears if a picture can't be read.",
        "Drop cursor shows \"copy\"; the overlay clears if the drag leaves the window or is cancelled; the message box gets focus after a drop.",
        "Codex checked dropped images, unsupported files and the 20 MB limit. Live dragging from the desktop has not been verified yet.",
    ]),
]


# ---------------------------------------------------------------- 1.3.0
story += [
    PageBreak(),
    P("v1.3.0 · 2026-11-10 · " + by("Claude") + " + " + by("Codex"), s_h1),
    P("New themes", s_h2),
    table([
        ["Theme", "Background", "Text / accent", "Window border"],
        ["Dark", code("#1e1f22"), "Default dark grey", "Follows Windows"],
        ["Light", code("#ffffff"), "Default light", "Follows Windows"],
        ["Cyberpunk", code("#0b0618"), "Neon cyan " + code("#05d9e8") + " with hot pink", "Pink " + code("#ff2a6d")],
        ["Hacker", code("#020602"), "Terminal green " + code("#33ff66"), "Green " + code("#1f8f3a")],
    ], [1.0 * inch, 1.1 * inch, 2.6 * inch, W - 4.7 * inch]),
    *bullets([
        by("Codex") + ": the two new color sets in " + code("web\\style.css") + " and the theme picker in Settings.",
        by("Claude") + ": " + code("ThemeChrome.cs") + " colors the Windows 11 caption bar, caption text and window border so the "
        "whole window matches the theme, not just the page. The window background matches too, so there's no white flash on open.",
        "Settings only accept the four known themes; anything else falls back to Dark.",
    ]),
]

# ---------------------------------------------------------------- 1.4.0
story += [
    P("v1.4.0 · 2026-11-11 · " + by("Claude") + " + " + by("Codex"), s_h1),
    P("Goal: blend the editor into the chat so both you and the AIs can see code and every change they make. "
      "Backup taken first: " + code("Backups\\XenoStudio-v1.3.0-20261111-212123") + ".", s_meta),
    P("Code workspace (UI by " + by("Codex") + ", backend by " + by("Claude") + ")", s_h2),
    *bullets([
        "A workspace panel beside the chat using <b>Monaco</b>, the VS Code editor, copied from XenoEdit into " + code("web\\lib\\vs") +
        ": syntax highlighting, folding, minimap; follows the app theme.",
        "<b>Code tiles:</b> long code blocks in replies become compact tiles (" + code("snippet.js · javascript · 9 lines") + "); click to open.",
        "<b>File links:</b> file names in messages and tool lines open the file in the workspace.",
        "<b>Edit code / Save file:</b> edit in place and save to disk. The chat gets a note such as "
        "<i>edited src\\cart.js in the editor (+1 −1)</i> so both AIs know on their next turn. <b>Save as…</b> for code blocks.",
        "<b>Attach code to chat:</b> send a selection or the whole draft as context with your next message.",
        "<b>Open in XenoEdit:</b> hands the file (or an unsaved block) to XenoEdit. Found automatically from a Desktop / Start Menu "
        "shortcut or a running XenoEdit; can also be set in Settings. Never hardcoded.",
        "<b>Live reload:</b> an open file reloads when an AI changes it, and asks first if you have unsaved edits.",
        "File reading is capped at 2 MB and refuses binary files; relative paths resolve against the work folders.",
    ]),
    P("AI change tracking (" + by("Claude") + ": " + code("EditTracker.cs") + ")", s_h2),
    *bullets([
        "Every file Claude or Codex edits during a reply gets a <b>change card</b> under that reply: who, which file, lines added/removed, "
        "created or deleted.",
        "Clicking a card opens a red/green <b>before/after diff</b>. Snapshots are stored per message in "
        + code("data\\snapshots\\") + " and deleted with the chat.",
        "<b>Claude</b> reports an edit just before making it, so its before-state is exact.",
        "<b>Codex</b> reports an edit only after it lands. To still get an accurate before-state, XenoStudio snapshots likely files at the "
        "start of every turn: paths mentioned in the prompt and files edited earlier in the chat. A Codex edit to any other file is marked "
        "<i>before not captured</i> and shows the result.",
        "Verified with real edits from both AIs (+2 −1 each).",
        "Limit: files changed through shell commands (a script writing a file) aren't reported by the CLIs, so they get no card.",
    ]),
    P("Backend requests added", s_h2),
    table([
        ["Request / event", "Does"],
        [code("previewFile"), "Read a file for the workspace (size and binary checks)."],
        [code("saveCode") + ", " + code("saveFile"), "Save a code block (Save as… dialog) or the open file; saving adds the edit note to the chat."],
        [code("openInXenoEdit") + ", " + code("xenoEditInfo"), "Find and launch XenoEdit."],
        [code("fileDiff"), "Return the before/after of one change card."],
        [code("watchFile") + ", " + code("unwatchFile"), "Watch the open file; pushes " + code("fileChanged") + " when it changes on disk."],
        [code("aiFile") + " (push)", "Tells the UI an AI is editing a file, live during the turn."],
    ], [2.2 * inch, W - 2.2 * inch]),
    P("Fixes and tooling", s_h2),
    table([
        ["Problem", "Fix"],
        ["The first 1.4.0 install half-failed: the app was reopened one second before files were copied, so they were locked.",
         code("_update\\apply-update.ps1") + " rewritten: if files are locked it waits for the app to close and retries (up to 20 times), "
         "and only restarts the app once every file is in place. The retry installed 1.4.0 cleanly."],
        ["Deleting a chat could leave its edit snapshots behind.", "Snapshots are removed first, then the chat file and its pictures."],
    ], [2.6 * inch, W - 2.6 * inch]),
    P("Documentation, GitHub and sharing", s_h2),
    *bullets([
        code("README.md") + " replaces README.txt: what the app is, a 2-minute demo, the full feature list, install/build steps, data and "
        "project layout. Screenshots in " + code("docs\\screenshots\\") + " use a made-up demo project and demo chat (no real files or chats).",
        "Source is on GitHub in a <b>private</b> repository. Before every push the code was scanned so only the name Xeno appears: "
        "no real name, email, address, location or machine paths (the build strips local paths from the exe too). "
        "Commits use a GitHub no-reply identity.",
        "One invited collaborator can view and download the repository.",
        "<b>Release v1.4.0</b> on the private repo: " + code("XenoStudio-v1.4.0-win-x64.zip") + " (3.7 MB), ready to run. "
        "It contains no " + code("data\\") + " folder (chats, settings, keys) and no CLI executables; users bring their own "
        + code("claude.exe") + " / " + code("codex.exe") + " and need the .NET 8 Desktop Runtime.",
        "Code signing (to stop the SmartScreen warning) is prepared but on hold: an individual certificate would show a legal name "
        "instead of Xeno.",
    ]),
]


# ---------------------------------------------------------------- 1.5.0
story += [
    PageBreak(),
    P("v1.5.0 · 2026-11-12 · " + by("Claude") + " + " + by("Codex"), s_h1),
    P("Goal: someone who downloads XenoStudio can get it running without help, using their own accounts. "
      "No credentials, sign-ins or settings of anyone else ship with the app.", s_meta),
    P("Setup wizard (UI by " + by("Codex") + ", backend by " + by("Claude") + ": " + code("Setup.cs") + ")", s_h2),
    *bullets([
        "Opens by itself on start when a CLI or sign-in that an enabled AI needs is missing; reopen any time from Settings. "
        "If everything is already in place, it never appears.",
        "<b>1 · Tools and AIs:</b> checks Node.js, Git for Windows, uv, the Claude Code CLI and the Codex CLI, with "
        "<b>Install</b> per item and <b>Install everything missing</b>.",
        "<b>2 · Sign in:</b> existing sign-ins are found by asking each CLI (" + code("claude auth status") + ", "
        + code("codex login status") + "), e.g. \"Signed in with a Claude account\", \"Signed in with ChatGPT\". "
        "If not signed in, <b>Sign in</b> opens the CLI's own login window, which opens the browser.",
        "<b>3 · MCP servers:</b> Desktop Commander, Windows-MCP, Playwright browser and Fetch. <b>Add</b> downloads the server, "
        "test-starts it with a real MCP handshake, and only then adds it. Import from Claude/Codex configs is there too.",
        "<b>4 · Ready:</b> lists anything still missing.",
        "Other AI seats (DeepSeek, Grok, local models) and their settings are left exactly as they were.",
    ]),
    P("Installs only after you agree", s_h2),
    table([
        ["Item", "Installed with", "Needs"],
        ["Node.js LTS", "winget " + code("OpenJS.NodeJS.LTS"), ""],
        ["Git for Windows", "winget " + code("Git.Git"), ""],
        ["uv", "winget " + code("astral-sh.uv"), ""],
        ["Claude Code CLI", "Anthropic's official installer (" + code("claude.ai/install.ps1") + ")", ""],
        ["Codex CLI", "npm " + code("@openai/codex"), "Node.js (added to the same prompt automatically)"],
    ], [1.4 * inch, 3.2 * inch, W - 4.6 * inch]),
    *bullets([
        "Every install shows a Windows Yes/No prompt listing everything that will be installed; Windows may also ask for permission (UAC).",
        "Without winget, the item offers <b>Get installer</b> (the official download page) instead.",
        "Progress streams into <i>Installation activity</i>; newly installed tools are found without restarting the app.",
        "Codex installed through npm is now found automatically.",
    ]),
    P("Packaging and privacy", s_h2),
    *bullets([
        "The download is now <b>self-contained</b>: .NET is included, so nothing needs installing before the first launch (65 MB zip).",
        "The zip has no " + code("data\\") + " folder (no chats, settings, MCP list or WebView2 profile) and no CLI executables or sign-in files. "
        "Each user signs in with their own Claude and ChatGPT accounts; sign-ins stay in their own " + code("~/.claude") + " and "
        + code("~/.codex") + ".",
        "Before release the zip was scanned for credential files, API keys, tokens, names, email, address and local paths: none found.",
        "API keys or tokens set as environment lines are now removed from settings exports.",
    ]),
    P("Verified", s_h2),
    *bullets([
        "Sign-in detection for both CLIs; missing-CLI detection on a copy without bundled CLIs.",
        "All four catalog MCP servers start and answer: Desktop Commander 25 tools, Windows-MCP 20, Playwright 25, Fetch 1.",
        "A fresh copy of the packaged app opens the wizard on its own and lists the missing items.",
        "Not run on this PC: the installers themselves (they would install a second copy of the CLIs).",
    ]),
    P("v1.5.1 · 2026-11-12 · " + by("Claude"), s_h1),
    P("A review of all 1.5.0 code before updating GitHub found three problems, now fixed:"),
    table([
        ["Problem", "Fix"],
        ["\"Install everything missing\" could crash if Node.js failed to install while Codex was also queued "
         "(the list of steps was changed while it was being walked).", "Skipped steps are tracked separately; the remaining installs carry on."],
        ["The wizard would pop up on every launch for someone who chose to skip an item.",
         "It pops up until setup is finished once; after that a missing item shows a short notice pointing to Settings."],
        ["\"Import existing MCP configuration\" in the wizard went into a closed Settings draft and was lost.",
         "It now opens Settings on the MCP page with the imported servers ready to review and save."],
    ], [3.2 * inch, W - 3.2 * inch]),
    *bullets([
        code("INSTALL.md") + ": download, SmartScreen, every wizard step, own-account sign-in, MCP servers, updating, troubleshooting. Included in the zip.",
        "README: a download link to the latest release at the top.",
    ]),
    P("v1.5.2 · 2026-11-12 · " + by("Claude"), s_h1),
    P("Skip option for sign-in", s_h2),
    *bullets([
        "When Claude and Codex are both already signed in, <b>Next</b> goes straight past the sign-in step; the step itself also has "
        "<b>Skip sign-in</b>.",
        "Each account that isn't detected has <b>Skip: already signed in</b>, for sign-ins XenoStudio can't see. It's remembered "
        "(" + code("skipLogin") + " in settings), the account shows as skipped, and the app stops asking about it. "
        "<b>Check sign-in again</b> undoes it.",
        "Tested in a copy with no sign-ins: Skip, Check sign-in again and the automatic step skip all work.",
    ]),
    P("v1.5.3 · 2026-11-12 · " + by("Codex"), s_h1),
    P("Workspace and drives", s_h2),
    *bullets([
        "<b>Settings → General → Workspace and drives</b> lists every drive on the user's own PC: local disks, USB / removable "
        "and mapped network drives. <b>Add drive</b> adds it to the work folders; <b>Add working folder…</b> picks a folder; "
        "<b>Refresh drives</b> re-reads the list. Drives that aren't connected are shown but greyed out.",
        "Several entries are allowed; the first is where the AIs start. Nothing about the developer's drives ships with the app.",
        "Fixed: <b>Clear folders</b> appeared on every Settings page instead of only General.",
    ]),
    P("v1.5.4 · 2026-11-12 · " + by("Claude"), s_h1),
    P("Review of 1.5.3 before updating GitHub", s_h2),
    table([
        ["Problem", "Fix"],
        ["Listing drives ran on the window's thread. Checking a mapped network drive that's offline can take a long time, "
         "which would freeze XenoStudio every time Settings opened.",
         "Drives are checked in the background, all at once, with a 3-second limit; a drive that doesn't answer in time is shown as not ready."],
        ["The drives section in README and INSTALL.md had damaged characters (arrows and apostrophes saved as \"?\") and sat at the end of the README.",
         "Rewritten: a Workspace and drives item in the README's feature list and its own section in INSTALL.md."],
        ["The setup bridge document didn't list the sign-in skip and drive requests.", code("docs\\SETUP-BRIDGE.md") + " updated."],
    ], [3.2 * inch, W - 3.2 * inch]),
    P("v1.5.5 · 2026-11-12 · " + by("Claude"), s_h1),
    P("Code workspace always follows the file you click", s_h2),
    table([
        ["Problem", "Fix"],
        ["Clicking a file that couldn't be opened (deleted since, a picture or other non-text file, unreadable) only changed a small "
         "status line. The previous file's name and code stayed in the panel, so it looked like the click did nothing.",
         "The panel switches to the clicked file's name immediately (\"Loading…\"). If it can't be opened, the old file is cleared and the "
         "panel says why (\"File not found\", \"That file isn't text\") with the copy and save buttons disabled."],
        ["The same applied to change cards whose recorded diff couldn't be loaded.", "Same handling."],
    ], [3.2 * inch, W - 3.2 * inch]),
    *bullets(["Tested: text file → missing file → picture → text file; the panel showed the right file or message each time.",
              "README: Codex added a responsibility and \"as is\" disclaimer."]),
    P("v1.5.6 - 2026-11-12 - " + by("Codex"), s_h1),
    *bullets([
        "File switching clears the previous editor and disables its action buttons immediately while the requested script loads.",
        "The previous file watcher is detached before loading another script, preventing an old file notification from pulling the panel back.",
        "The same loading state applies to change-card diffs. Missing or non-text files show their own error instead of old code.",
        "Browser regression checks cover switching scripts, overlapping requests, missing files and old-file notifications. Release built and packaged without local settings or credentials.",
    ]),
    P("v1.5.7 · 2026-11-12 · " + by("Claude"), s_h1),
    P("Update XenoStudio from GitHub (" + code("Updater.cs") + ")", s_h2),
    *bullets([
        "<b>Settings → Runtime → XenoStudio updates</b>: shows whether a newer release is out, with <b>Check for updates</b>, "
        "<b>Update XenoStudio</b> and <b>Release notes</b>.",
        "<b>Check GitHub for new XenoStudio versions at start-up</b> (on by default): a short notice when a new release is out. "
        "It never installs anything by itself.",
        "<b>Update XenoStudio</b> asks first (Yes/No, with the version and download size), downloads the release zip and checks it "
        "against GitHub's SHA-256 checksum; a mismatch is deleted. It also checks the zip really contains that version.",
        "XenoStudio then closes, a small script copies the new files in and reopens the app. While it copies, XenoStudio refuses to "
        "start, so an early click can't leave a mix of versions. Locked files are retried.",
        code("data\\") + " (chats, settings, sign-ins) and " + code("runtime\\") + " are never touched. The previous version is kept in "
        + code("_selfupdate\\previous") + " and the log in " + code("_selfupdate\\update.log") + ".",
        "Folders XenoStudio can't write to (such as Program Files) get a clear message pointing to the release page instead.",
        "Tested end to end: a copy built as 1.5.0 found 1.5.6 on GitHub, downloaded and verified it, closed, installed and reopened "
        "as 1.5.6 in about 4 seconds, with its data and runtime\\ kept and 1.5.0 backed up.",
    ]),
    P("README and INSTALL.md: new Updating sections.", s_meta),
]

# ---------------------------------------------------------------- files, backups, open items
story += [
    P("Files changed", s_h1),
    table([
        ["File", "Version", "What changed"],
        [code("Settings.cs"), "1.1.0, 1.2.0", "Folder lists (PrimaryWorkDir, ExtraDirs), uploads path, stale-MCP refresh, settings version upgrade, retry default."],
        [code("Agents.cs"), "1.1.0", "Pipe-error fix, folder lists for both CLIs, pictures passed to the CLIs."],
        [code("MainWindow.xaml.cs"), "1.1.0, 1.2.0", "Picture save/serve/prompt/export, folder lists everywhere, second-launch hand-off, window bounds, crash-proof turns, title."],
        [code("App.xaml / App.xaml.cs"), "1.2.0", "Single-instance mutex + named pipe, crash log, app now opens the window itself."],
        [code("XenoStudio.csproj"), "1.1.0, 1.2.0", "Version numbers; " + code("_update\\") + " excluded from compile."],
        [code("web\\app.js"), "1.1.0, 1.2.0", "Picture paste/drop/attach, thumbnails, encoding fix (Claude); drop overlay and file-type fixes (Codex)."],
        [code("web\\index.html"), "1.1.0", "+ Picture button, attachment strip, encoding fixes, folder hints."],
        [code("web\\style.css"), "1.1.0, 1.2.0", "Attachment and thumbnail styles (Claude); drop overlay (Codex)."],
        [code("web\\settings.js"), "1.1.0", "Multi-folder labels, Add working folder…, Clear folders."],
        [code("runtime\\*.exe"), "1.1.0", "Claude Code 2.1.321, Codex 0.167.0-alpha.3.2 and helpers."],
        [code("ThemeChrome.cs"), "1.3.0", "New: theme colors for the Windows title bar, caption text and border."],
        [code("CodePreview.cs"), "1.4.0", "New: workspace file reading, Save as, finding and opening XenoEdit, preview temp files."],
        [code("EditTracker.cs"), "1.4.0", "New: change cards, before/after snapshots, diffs."],
        [code("Agents.cs"), "1.4.0", "Reports file edits from Claude (before) and Codex (started / completed)."],
        [code("MainWindow.xaml.cs"), "1.3.0, 1.4.0", "Theme chrome; workspace requests, file watching, per-turn edit tracking, editor edit notes, chat delete cleanup."],
        [code("Settings.cs"), "1.3.0, 1.4.0", "Theme validation; XenoEdit path setting."],
        [code("web\\preview.js"), "1.4.0", "New (Codex): workspace, code tiles, change cards, diff view."],
        [code("web\\lib\\vs\\"), "1.4.0", "Monaco editor, copied from XenoEdit."],
        [code("web\\style.css") + ", " + code("web\\settings.js"), "1.3.0, 1.4.0", "Cyberpunk / Hacker themes, workspace styles, XenoEdit path field (Codex)."],
        [code("_update\\apply-update.ps1"), "1.4.0", "Retry loop when files are locked."],
        [code("README.md") + ", " + code("docs\\screenshots\\"), "1.4.0", "New guide with demo and screenshots."],
        [code("Setup.cs"), "1.5.0", "New: setup checks, installs, sign-in, MCP catalog and handshake test."],
        [code("web\\setup.js"), "1.5.0", "New (Codex): the setup wizard."],
        [code("_dev\\package.ps1"), "1.5.0", "Self-contained package by default; CLIs optional."],
    ], [1.6 * inch, 0.95 * inch, W - 2.55 * inch]),
    P("Backups and how to roll back", s_h1),
    table([
        ["Backup", "Contains"],
        [code("Backups\\XenoStudio-v1.1.0-20261109-175842"), "Whole app at 1.1.0: source, build, runtime, chats, settings."],
        [code("_update\\old-dist-20261109-175448"), "App files of 1.0.0, replaced by the 1.1.0 install."],
        [code("_update\\old-dist-20261109-180615"), "App files of 1.1.0, replaced by the 1.2.0 install."],
        [code("_update\\old-runtime\\"), "Claude Code 2.1.312 and Codex 0.166 executables."],
        [code("_update\\apply-update.log"), "Log of each install."],
        [code("_update\\old-dist-20261110-193553"), "App files of 1.2.0, replaced by the 1.3.0 install."],
        [code("Backups\\XenoStudio-v1.3.0-20261111-212123"), "Whole app at 1.3.0, taken before the 1.4.0 work."],
        [code("_update\\old-dist-20261111-214311"), "App files of 1.3.0, replaced by the 1.4.0 install."],
    ], [3.1 * inch, W - 3.1 * inch]),
    Spacer(1, 4),
    P("To roll back: close XenoStudio and copy a backup's files over " + code("dist\\") + " (leave " + code("dist\\data\\") + " in place)."),
    P("Not yet verified", s_h1),
    *bullets([
        "Dragging a picture from the desktop into the running app (Codex's note).",
        "Clicking through the picture UI inside the app by hand: the code paths were tested outside the app.",
        "The crash log and crash-proof turns: they only run when something actually crashes.",
        "The Save as… dialog and launching XenoEdit from the workspace, clicked by hand in the installed app.",
    ]),
]

doc = SimpleDocTemplate(OUT, pagesize=letter, leftMargin=0.75 * inch, rightMargin=0.75 * inch,
                        topMargin=0.75 * inch, bottomMargin=0.85 * inch,
                        title="XenoStudio Change Notes", author="Claude and Codex for Xeno", subject="v1.0.0 to v1.5.7", invariant=1)
doc.build(story, onFirstPage=footer, onLaterPages=footer)
print(OUT)
