# Installing XenoStudio

## 1. Download
Open the repository's **Releases** page and download `XenoStudio-vX.Y.Z-win-x64.zip` from the latest release.
You need Windows 10 or 11 (64-bit). .NET is included in the zip; WebView2 is built into Windows 11
(on Windows 10, get the [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) if the window stays blank).

## 2. Unzip and run
Unzip to any folder you can write to (for example `Documents\XenoStudio`; not `Program Files`) and run `XenoStudio.exe`.
Everything the app saves stays in that folder's `data\` subfolder.

Windows SmartScreen may say *"Windows protected your PC"* because the app isn't code-signed yet: click **More info → Run anyway**.

## 3. The setup wizard
If something is missing, the wizard opens by itself. You can open it any time from **Settings → Setup tools and sign-in**.

**Step 1 · Tools and AIs**

| Item | What it's for | Installed from |
|---|---|---|
| Claude Code CLI | Runs Claude | Anthropic's official installer |
| Codex CLI | Runs Codex | npm (`@openai/codex`); brings Node.js along if needed |
| Node.js | Codex install, MCP servers started with `npx` | winget |
| Git for Windows | Claude Code runs commands through Git Bash | winget |
| uv | Python-based MCP servers (`uvx`) | winget |

Click **Install** on an item, or **Install everything missing**. A Yes/No prompt lists exactly what will be installed before
anything runs, and Windows may ask for permission. If winget isn't on the PC, **Get installer** opens the official download page.
Already-installed tools are found and reused.

**Step 2 · Sign in**

XenoStudio uses **your own** accounts. No sign-ins, keys or settings are included with the app.

- If Claude Code or Codex is already signed in on this PC, it's detected and shown as *Signed in with a Claude account* /
  *Signed in with ChatGPT*. Nothing else to do.
- If not, **Sign in** opens that CLI's own login window, which opens your browser. Sign in there, then close the window.
  Claude uses a Claude plan (Pro or Max); Codex uses a ChatGPT plan.
- **Skip**: if both are already signed in, the wizard skips this step by itself (or click **Skip sign-in**). If you're signed
  in some way XenoStudio can't detect, click **Skip: already signed in** on that account and it stops asking.
  **Check sign-in again** undoes it.
- Prefer an API key? Put it in **Settings** (Claude: environment `ANTHROPIC_API_KEY`; Codex: run `codex login --with-api-key`).

Sign-ins are stored where the CLIs always keep them (your user folder's `.claude` and `.codex`), not in XenoStudio.

**Step 3 · MCP servers (optional)**

Extra tools the AIs can use. **Add** downloads the server, test-starts it, and only adds it if it answers.

| Server | Gives the AIs | Needs |
|---|---|---|
| Desktop Commander | Files, search, long-running terminal processes | Node.js |
| Windows-MCP | Seeing and using Windows apps (screenshots, clicks, typing) | uv |
| Playwright browser | A real browser to open, click through and read | Node.js |
| Fetch | Web pages as readable text | uv |

**Import existing MCP configuration** copies servers you already set up for Claude or Codex.

**Step 4 · Ready**

Lists anything still missing. Click **Finish setup**.

## 4. First chat
Open **Settings → General** to choose your work folder(s) and permissions, then type a message. With **Send to: Auto**
both AIs answer. See the README for a short walkthrough.

## Updating
Download the new zip and unzip it **over** the old folder (keep `data\`), or unzip to a new folder and copy `data\` across.

## Troubleshooting
| Problem | Fix |
|---|---|
| "Not installed" right after installing | Click **Check again**. Some installers only finish updating PATH after a moment. |
| Sign-in window closed but still "Not signed in" | Click **Check again**; if it persists, run the sign-in again and finish it in the browser. |
| An MCP server fails to add | The error shows the server's last output. Check its "Needs" item in step 1 is installed. |
| Codex can't write files | **Settings → Codex → Sandbox**: choose *workspace-write*. |
| Something else | `data\crash.log` has details. |


## Workspace and drives (1.5.3)

In Settings ? General ? Workspace and drives, choose an available drive and click **Add drive**, or use **Add working folder** to choose a directory, including accessible network folders. Local, USB, removable and mapped network drives are detected on the user?s computer. Unavailable drives are disabled; reconnect and use Refresh drives. Multiple entries are supported; the first is the starting folder. Save settings to apply. Windows permissions and the selected assistant access mode still apply. No developer drive list, local settings or credentials are included in the download.
