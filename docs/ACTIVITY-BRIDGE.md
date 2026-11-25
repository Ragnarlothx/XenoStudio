# Activity panel bridge (host → page)

The Activity panel shows what each AI is thinking and doing during the current chat. The backend sends this data and the web UI renders it.

## Setting

`settings.showThinking` (bool, default `true`) is available in **Settings → Chat → Show thinking in Activity**. When it's on:

- **Claude:** runs with `--thinking-display summarized`. Headless Claude Code leaves thinking text empty unless this flag is set. If an older custom `claude.exe` rejects the flag, the host retries once without it and leaves it off until the app restarts.
- **Codex:** runs with `-c model_reasoning_summary="detailed"`. Its `reasoning` items become thinking steps.
- **DeepSeek and Grok:** `reasoning_content` deltas become thinking steps. The older `showReasoning` "thinking…" tool line is unchanged.

A `--thinking-display` in Claude extra args, or a `model_reasoning_summary` in Codex extra args, overrides the host's value.
Turning the setting off hides thinking summaries in the panel and pauses their collection. Saved summaries are visible again when the setting is enabled.

## Saved data (`chat.messages[]`)

| Field | Type | Meaning |
|---|---|---|
| `trace` | `[{kind, text, ts}]` or absent | Ordered steps for one AI reply. Absent on old messages and on Xeno/system/shell messages. |
| `trace[].kind` | `"think"` \| `"tool"` \| `"retry"` | `think` is a thinking or reasoning summary in markdown, often with `**bold**` headings. `tool` is a tool call line, the same text as `msg.tools`. `retry` is a retry or session-restart note. |
| `trace[].ts` | ms epoch | When the step started. |
| `doneTs` | ms epoch | When the reply finished (0 while running). The duration is `doneTs - ts`. |

Consecutive thinking chunks merge into a single `think` step. A tool call closes that step, so the next chunk starts a new one. Each reply keeps up to 120,000 characters of trace.

## Live messages

```jsonc
{ "type": "trace", "id": "<msgId>", "who": "claude", "kind": "think", "text": "…", "ts": 1764000000000, "append": true }
```

- `append: true` adds `text` to the end of that message's last step, which is always a `think` step.
- `append: false` starts a new step with this `text`.
- `"reset"` (sent before a retry) still clears `tools`. **It doesn't clear `trace`**, so the history shows the retry.
- `"start"` and `"done"` identify the owning conversation with `chatId`. `"done"` carries the full `msg`, including `trace` and `doneTs`, to replace the live copy.

The `status` message now also carries:

```jsonc
{ "type": "status", "agents": {"claude":"running","codex":"queued",...}, "busy": true,
  "running": "claude", "runningSince": 1764000000000, "chatId": "<id>", "replyId": "<msgId>", "queue": ["codex"] }
```

`runningSince` is 0 when no AI is running in the selected chat. `replyId` lets the page resume showing a reply that was already in progress when the page loaded.
