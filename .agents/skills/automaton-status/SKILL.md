---
name: automaton-status
description: Use when the owner asks for a status ("Status?", "End of day") or a morning report ("Morning", "What happened overnight?"). Answers in chat from real GitHub, queue and gate data; no page.
---

# Status in chat

Answer in the chat. No artifact or page.

| Owner says | Report |
|---|---|
| "Status?", "End of day", any time | **Day** |
| "Morning", "What happened overnight?" | **Morning** (adds Overnight and Inbox) |

## Gather (real data only)

Never fill a row from memory of the session. Each row comes from one of these, and links to it:

| Section | Source |
|---|---|
| Merged | `gh pr list -R <repo> --state merged --search "merged:>=<day>"` for each repo in the config |
| Closed as live | `gh issue list --state closed --search "closed:>=<day>"` |
| Filed | `gh issue list --state all --search "created:>=<day>"`, grouped by parent issue |
| Waiting | open PRs (skip dependency bumps) and `python .agents/tools/merge-gate.py <pr>`: say what each needs |
| Agents now | `ssh <runner> '~/bin/agent-queue status'` for each runner in the config |
| Overnight (Morning) | the queue's finished tasks since 20:00 the day before, each with the last lines of its log (`~/agent-queue/logs/<name>.log`), and the newest morning report |
| Inbox (Morning) | board items with Status `Inbox` on the agent board in the config, newest first |
| Health | test server and production answer (`/api/install/status`), versions from `automaton installation-status` |
| Needs you | owner actions, gate results that wait on the owner, and anything blocked on a decision |

## Write

- **Needs you** first, then Agents now, Waiting, Merged, Closed, Filed (Morning: Overnight and Inbox after Needs you).
- Time (UTC) and each environment's version in the first line.
- Short bullets, one item each, with the issue or PR number. Empty sections get one line ("Nothing ran overnight").
- Group long lists (Filed) by parent issue, several numbers per bullet.
