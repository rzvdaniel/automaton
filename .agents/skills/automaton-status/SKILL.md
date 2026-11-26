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
| Agents now | agent board items with Status `Ready for agents`, `Running` or `Verify` (`gh project item-list 17 --owner rzvdaniel`); runner-01 picks up `Ready for agents` through the GitHub agent runner. `Verify` means the PR is open or already merged and waiting for release: read each card's linked PR state (`gh pr list --state all --search "<issue> in:body"`), never only open PRs, and report it as "merged, not released" or "open: <what blocks it>" |
| Overnight (Morning) | `Agent issue` runs since 20:00 the day before (`gh run list -R rzvdaniel/automaton --workflow "Agent issue" --created ">=<yesterday>T20:00:00Z"`) with their result, board items moved to `Verify`/`Done`, and the newest morning report |
| Inbox (Morning) | board items with Status `Inbox` on the agent board in the config, newest first |
| Health | test server and production answer (`/api/install/status`), versions from `automaton installation-status` |
| Needs you | owner actions, gate results that wait on the owner, and anything blocked on a decision |

## Write

- **Needs you** first, then Agents now, Waiting, Merged, Closed, Filed (Morning: Overnight and Inbox after Needs you).
- Time (UTC) and each environment's version in the first line.
- Short bullets, one item each, with the issue or PR number. Empty sections get one line ("Nothing ran overnight").
- Group long lists (Filed) by parent issue, several numbers per bullet.
