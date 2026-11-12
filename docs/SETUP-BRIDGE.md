# Setup wizard: bridge contract (backend = Setup.cs + MainWindow.xaml.cs)

Requests are `{cmd, rid, ...}`; replies are `{type, rid, ok, data | error}` like the workspace requests.

## Push from the app
| type | when | payload |
|---|---|---|
| `setupNeeded` | right after `ready`, if a CLI or sign-in for an **enabled** AI is missing | `{items: SetupItem[], firstRun}`: open the wizard |
| `setupLog` | while installing / test-starting an MCP server | `{id, line}`: id is a SetupItem id or `mcp:<catalogId>` |
| `settings` | after an API key, MCP install or setupDone | same as today |

If everything is already in place on first run, the app silently marks setup done and sends nothing.

## Requests
| cmd | args | reply data |
|---|---|---|
| `setupScan` | | `{items: SetupItem[], setupDone, winget}` (takes 2-5 s: it runs each CLI) |
| `setupInstall` | `{id}` or `{ids:[...]}` | `{items: SetupItem[]}` rescanned rows. The app shows a native Yes/No first listing everything it will install (prerequisites added automatically, e.g. Codex pulls in Node.js). `ok:false, cancelled:true` if the user says No. Can take minutes; stream `setupLog` lines meanwhile. One install at a time. |
| `setupLogin` | `{id: "claude"\|"codex"}` | `{item}`: opens the CLI's own sign-in in a console window (it opens the browser). Replies when that window closes. |
| `setupApiKey` | `{id: "claude"\|"codex", key}` | `{item}`. Claude: saved as ANTHROPIC_API_KEY in Claude's environment (stripped from settings exports). Codex: `codex login --with-api-key`. Use a password field; never echo the key. |
| `setupDone` | | none (no reply); saves `setupDone:true` |
| `mcpCatalog` | | `{servers: McpCatalogEntry[], node, uv}`: node/uv = whether npx/uvx are available |
| `mcpInstall` | `{id}` | `{tools, servers}`: test-starts the server (downloads it the first time, up to ~4 min), adds or re-enables it in Settings → MCP. Error text includes the server's last output lines. |

## Shapes
```
SetupItem       { id, group: "tool"|"cli"|"login", name, why, required, ok, status: "ok"|"missing"|"warn",
                  detail, actions: ("install"|"login"|"apikey"|"open")[], url }
McpCatalogEntry { id, name, desc, needs: "node"|"uv", installed }
```
Item ids: `node`, `git`, `uv`, `claude`, `codex`, `claude-login`, `codex-login`.
`open` = no winget on this PC: send `openUrl` with `item.url`.
Catalog ids: `desktop-commander`, `windows-mcp`, `playwright`, `fetch`.

## Suggested flow
1. **Tools and AIs**: the checklist with Install buttons, plus **Install everything missing** (`setupInstall {ids}`).
2. **Sign in**: Sign in (browser) / Use API key for Claude and Codex. Detected sign-ins show green: "Signed in with a Claude account", "Signed in with ChatGPT".
3. **MCP servers**: the catalog with Add buttons and a log; **Import from Claude/Codex** (existing `importMcp`).
4. **Done**: `setupDone`. A **Setup** button in Settings reopens the wizard any time.
