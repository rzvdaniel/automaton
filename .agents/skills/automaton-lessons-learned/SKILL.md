---
name: automaton-lessons-learned
description: Use for the nightly (or on-demand) run that finds what went wrong (queue logs, failed gates and releases, stalled tasks) and files each new root cause as a lesson issue in the agent board's Inbox for the owner to triage.
license: Apache-2.0
---

# Automaton lessons learned

Turns what went wrong into issues the owner can pick as next work. It files issues; it does not fix them.
Works on Desktop and on a headless agent host; `gh` is the only hard requirement.

## 1. Collect (since the last run)

Collect the last 7 days unless the owner names another period.

- Queue logs, if `~/agent-queue/logs` exists: `grep -il "error\|failed\|cancel\|stall"` on logs newer than the last
  run; read only the tails.
- `gh run list --status failure --limit 30` for `Merge gate` and release runs; issues labelled `verification-failed`.

## 2. Decide

- One root cause per incident; merge incidents that share one.
- **Skip** one-offs with no lasting fix.
- **Search first:** `gh issue list --state all --label lesson --search "<keywords>"` (and without the label). An
  existing issue gets a comment with the new incident, not a twin.

## 3. File

Each new root cause: one issue per [write-issue](../automaton-write-issue/SKILL.md), label `lesson`. Example = what
happened, with links; Suggestion = the lasting fix and where it goes (skill or doc, gate or tool code, owner-only rule,
installation fact). Put it on the agent board with Status **Inbox** (`gh project item-add <board> --owner <owner> --url <issue>`,
then `.agents/tools/agent-board.sh move <repo> <n> Inbox`). Never move it to Ready: the owner decides.

## 4. Report

End with a short list in the job output: issues filed, issues commented, what was skipped and why (one line each).

## Schedule

None: run it when the owner asks.
