---
name: automaton-lessons-learned
description: Use for the nightly (or on-demand) run that turns the lessons inbox, queue logs and failed gates into at most three concrete fixes, so the same problem never stops the agents twice.
license: Apache-2.0
---

# Automaton lessons learned

Turns what went wrong into fixes. Works on Desktop and on runner-01; `gh` is the only hard requirement.

## 1. Collect (since the last run)

Last run = date of your own newest "Lessons run" reply on the open issue labelled `lessons` (the inbox; none yet means everything).

- New comments on the inbox.
- Queue logs, if `~/agent-queue/logs` exists: `grep -il "error\|failed\|cancel\|stall" ` on logs newer than the last run; read only the tails.
- `gh run list --status failure --limit 30` for `Merge gate` and release runs; issues labelled `verification-failed`; tasks the orchestrator reported as stalled.

## 2. Decide

- One root cause per incident; merge incidents that share one.
- **Skip** one-offs, anything the inbox already marks "fixed for good", and anything an earlier "Lessons run" reply covers.
- **Search first:** `gh issue list --state all --search "<keywords>"` and `grep -ri` in `.agents/` and `docs/`. An existing issue gets a comment, not a twin.
- At most **3 changes** per run; take the ones that stopped agents longest. Leave the rest for the next run.

## 3. Route each fix

| Root cause | Where it goes |
| --- | --- |
| Skill or doc wording | small PR ([write-PR](../automaton-write-pr/SKILL.md)); the PR follows [PR review](../pr-review/SKILL.md) |
| Product, gate code or tool change | issue ([write-issue](../automaton-write-issue/SKILL.md)) |
| Owner-only rule (`AGENTS.md`, merge-gate rules, access, production) | listed in the reply for the owner; never edited here |
| Installation fact (cron, sudo, paths, preamble of one host) | orchestrator config, noted in the reply |

Never edit `AGENTS.md` or the merge-gate rules.

## 4. Reply

One comment on the inbox, starting `Lessons run YYYY-MM-DD`: each change with its link, what was skipped and why (one line each), and the owner list. Reply even when nothing changed, so the next run has its date.

## Schedule

Nightly on runner-01: `0 3 * * * ... ~/bin/agent-queue add lessons-$(date +%Y%m%d)` with a prompt that says to follow this skill.
