---
name: automaton-status
description: Use when the owner asks for a status ("Status?", "End of day") or a morning report ("Morning", "What happened overnight?"). Publishes a live status page as an artifact from real GitHub, queue and gate data.
---

# Status pages

Two live pages, each with a fixed link the owner bookmarks. Their URLs are in `.agents/orchestrator.config.md` (`Status pages`). Update the same page every time; never make a new one unless the config has no URL yet (then publish once and record the URL).

| Owner says | Page | Template |
|---|---|---|
| "Status?", "End of day", any time | **Day** | `day.html` |
| "Morning", "What happened overnight?" | **Morning** | `morning.html` |

Publishing needs the Artifact tool, so this runs in the Desktop orchestrator session.

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
| Needs you | owner actions from the handover issue, gate results that wait on the owner, and anything blocked on a decision |

## Build

- Copy the template, keep its CSS and section order, and replace only the content of each section. Empty sections say so in one line ("Nothing ran overnight"); never delete them.
- **Needs you** is always first. One row per item: dot, plain words, link. States: `d-ok` done, `d-run` running, `d-wait` waiting, `d-you` needs the owner, `d-bad` blocked or failed.
- Counts at the top match the rows below. Put the time (UTC) in the header and the footer.
- Group long lists (Filed) by parent issue, three to six rows per group, several numbers per row.

## History (Day page)

The Day page reads `history.json` published next to it: one array, newest first, of `{"day":"2026-11-20","counts":{...},"top":["…","…"],"links":[…]}`. On "End of day", read the current file from the artifact (`action: "read"`, `path: "history.json"`), add today's entry at the front, keep the last 60 days, and publish it with the page. Other status requests leave it as it is.

## Answer

One line with the link and the top "Needs you" item. The page holds the rest.
