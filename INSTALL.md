# Installing XenoStudio

## 1. Download

Open [Latest release](https://github.com/Ragnarlothx/XenoStudio/releases/latest), expand **Assets** and download
`XenoStudio-vX.Y.Z-win-x64.zip`. Do not choose **Source code (zip)** or **Code > Download ZIP** unless you intend to build it.
You need Windows 10 or 11 x64. .NET is included. The app also needs Microsoft's
[WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/); install it if missing.
Use an internet connection for downloads and cloud assistants, and your own eligible provider accounts.
XenoStudio does not supply a subscription, credits or credentials.

## 2. Unzip and run

Use **Extract All** to unpack the whole ZIP into a writable folder (for example `Documents\XenoStudio`; not
`Program Files`) and run the extracted `XenoStudio.exe`. Keep the other files and the `web` folder beside it.
Do not run from inside the ZIP or move only the executable. XenoStudio keeps settings and chat data under `data\`;
vendor tools, sign-ins, exports and edited projects can use other locations.

Windows SmartScreen may say *"Windows protected your PC"* because the app isn't code-signed. Verify the download came
from this repository. If you choose to trust it, **More info > Run anyway** may be available. A managed PC may require
administrator approval; do not disable security software to bypass it.

## 3. The setup wizard

Until setup is finished once, the wizard opens when a required tool or login is missing. If everything is found, it
may not appear. Reopen it any time from **Settings > Setup tools and sign-in**. After finishing once, a missing item
can produce a notice instead of reopening the wizard.

**Step 1 · Tools and AIs**

| Item | What it's for | Installed from |
|---|---|---|
| Claude Code CLI | Runs Claude | Anthropic's official installer |
| Codex CLI | Runs Codex | npm (`@openai/codex`); brings Node.js along if needed |
| Node.js | Codex install, MCP servers started with `npx` | winget |
| Git for Windows | Claude Code runs commands through Git Bash | winget |
| uv | Python-based MCP servers (`uvx`) | winget |

Click **Install** on an item, or **Install everything missing** (which can include optional tools). A Yes/No prompt lists what will be installed before
anything runs, and Windows may ask for permission. If winget isn't on the PC, **Get installer** opens the official download page.
Already-installed tools are found and reused.

**Step 2 · Sign in**

XenoStudio uses **your own** accounts. No sign-ins, keys or settings are included with the app.

- If Claude Code or Codex is already signed in on this PC, it's detected and shown as *Signed in with a Claude account* /
  *Signed in with ChatGPT*. Nothing else to do.
- If not, **Sign in** opens that CLI's own login window and browser flow. Complete it, wait for success in the login
  window, and return to setup. Browser website sign-in alone does not necessarily authenticate the CLI.
- **Skip**: if both are already signed in, the wizard skips this step by itself (or click **Skip sign-in**). If you're signed
  in some way XenoStudio can't detect, click **Skip: already signed in** on that account and it stops asking.
  **Check sign-in again** undoes it.
- **Skip is not sign-in.** It does not create credentials or bypass account limits; an AI turn can still fail.
  Use your own supported account. Advanced API credentials remain in provider settings and may have separate billing.

The CLIs manage browser-login credentials in their own authentication storage. Never share sign-in folders or paste
passwords, API keys or device-login codes into the chat. Review [Your data](README.md#your-data) before sharing an export.

**Step 3 · MCP servers (optional)**

Extra tools the AIs can use. Skip this step for a simple first chat. Clicking **Add** authorizes downloading and
test-running that server, then enabling it if the test succeeds. The test checks connectivity, not package safety.

| Server | Gives the AIs | Needs |
|---|---|---|
| Desktop Commander | Files, search, long-running terminal processes | Node.js |
| Windows-MCP | Seeing and using Windows apps (screenshots, clicks, typing) | uv |
| Playwright browser | A real browser to open, click through and read | Node.js |
| Fetch | Web pages as readable text | uv |

**Import existing MCP configuration** copies servers you already set up for Claude or Codex.

**Step 4 · Ready**

Lists anything still missing. Click **Finish setup**. This does not install or sign into skipped components.

## 4. First chat

Open **Settings > General** to choose your project folder(s) and display name. Permissions are on the separate
**Claude** and **Codex** pages. Save settings, choose your intended AI in **Send to**, and type a message.
**Before giving the AIs full access, read [Access levels and rules for the AIs](README.md#access-levels-and-rules-for-the-ais)**
and put your rules in **Settings → General → Instructions for both assistants**. With **Send to: Auto**
enabled primary AIs can answer in turn. If you use only one, disable the other on its settings page.
Try: **"Tell me which working folder you were given. Do not run commands or change any files."**
Then try a non-sensitive file before requesting changes. See the README for the code-workspace walkthrough.

## Workspace and drives

**Settings → General → Workspace and drives** lists the drives on your PC: local disks, USB and removable drives, and mapped
network drives. Pick one and click **Add drive**, or **Add working folder…** to choose a specific folder (network folders work too).
Drives that aren't connected are greyed out; reconnect them and click **Refresh drives**. You can add several; the first one is
where the AIs start. Click **Save settings** to apply. Windows permissions and the access mode you chose for each AI still apply.

## Updating

Easiest: **Settings → Runtime → Update XenoStudio**. It downloads the latest release, checks it against GitHub's checksum, then closes, installs and reopens by itself; chats, settings and sign-ins are kept. XenoStudio also tells you at start-up when a new version is out (switch that off in **Settings → Runtime**).

Save editor drafts and wait for AI work to finish before updating. Do not relaunch the app while it is copying files.
By hand: close XenoStudio, keep a private backup, extract a new release into a writable folder and copy `data\`
and any existing `runtime\` into it while both copies are closed. Keep the old folder until the new one works.
The app updater does not also update every CLI or MCP package. See [Updating](README.md#updating) for recovery and logs.

## Troubleshooting

| Problem | Fix |
|---|---|
| "Not installed" right after installing | Click **Check again**. Some installers only finish updating PATH after a moment. |
| Sign-in window closed but still "Not signed in" | Click **Check again**; if it persists, run the sign-in again and finish it in the browser. |
| An MCP server fails to add | The error shows the server's last output. Check its "Needs" item in step 1 is installed. |
| Codex can't write files | Check Windows access and the work folder, then **Settings > Codex > Access**; *workspace-write* is for intended project edits. |
| Something else | Follow the [symptom-by-symptom guide](README.md#common-setup-problems). Review and redact logs before sharing. |

For safe bug reports, see [Getting help without sharing private information](README.md#getting-help-without-sharing-private-information).
Do not upload your used app folder, full settings exports, sign-in files or unreviewed screenshots.


## MCP add-ons catalog

Open **Settings > MCP > MCP add-ons** to browse Desktop Commander (files and terminal), Windows-MCP (desktop control), Playwright (browser automation) and Fetch (web content). Cards show the server description, requirements and enabled status. **Add server** downloads if needed, tests the server and saves/enables it immediately; **Test and enable** checks an existing entry. Installation activity appears below the catalog. If Node.js or uv is missing, open **Install requirements / open Setup** and approve the required installer there, then refresh the catalog. Custom servers and Import remain below the catalog; manual configuration edits still require Save settings. Each server runs locally with the permissions of its process.


## Installation paths and one-click installation

Open **Settings > Installation** to select the Claude Code, Codex and XenoEdit executables or leave them blank for auto-detection. **Add tool folder** adds an installed dependency directory to the additional tool folders list, one absolute path per line (for example the folder containing node/npm/npx, Git or uv/uvx). These directories are searched first inside XenoStudio; your Windows user/system PATH is not changed. Clear the list to return to normal detection. Existing bundled-CLI preference switches remain available.

**Check saved paths** saves the current settings and checks installed tools and sign-in status. **Install missing components** saves, checks, then requests confirmation before installing missing supported tools. If automatic installation is unavailable, a Get installer button opens the official download page. Installer destinations follow each vendor installer; choosing paths here does not relocate applications. Sign-in stays in Setup and uses your own accounts. Local path settings are not bundled in public downloads.
