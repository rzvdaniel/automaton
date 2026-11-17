---
name: automaton-lessons-learned
description: Use on the nightly lessons run, or when asked to learn from what went wrong, to turn the lessons inbox, queue logs and failed gate runs into concrete fixes, so the same problem never stops the agents twice.
license: Apache-2.0
---

# Automaton lessons learned

One run reads what went wrong since the last run and makes at most 3 changes. It never merges its own PRs without the gate, releases or touches production.

## 1. Read since the last run

The last run is the date in your newest reply on the open issue labelled `lessons` (`gh issue list --label lessons`); none means the whole inbox. Read only what is newer:

- new comments on the lessons issue;
- `~/agent-queue/logs` (tail of failed or long tasks, not whole logs; never print credentials);
- failed `Merge gate` and release runs (`gh run list --status failure`);
- issues labelled `verification-failed`, and tasks stalled in the queue.

## 2. One root cause per incident

Group lines that share a cause. Skip one-offs (a typo, a flaky network blip, something already fixed for good). Search open and closed issues, PRs and skills first (`gh search issues`, `grep -r .agents/skills`): if the fix exists, skip it.

## 3. Put the fix where it belongs

| Cause | Fix |
| --- | --- |
| Skill or doc wording unclear or missing | Small PR editing that skill or doc ([write-PR skill](../automaton-write-pr/SKILL.md)); gate as usual |
| Product, script or tool behaves wrongly | Issue via [write-issue](../automaton-write-issue/SKILL.md), on the board |
| Owner-only rule (`AGENTS.md`, merge-gate rules, access, production) | List it for the owner in the reply; never edit `AGENTS.md` or the merge-gate rules yourself |
| Installation fact (machine, queue, cron, preamble) | Note it for the orchestrator configuration in the reply |

Over 3 changes: take the ones that stopped work most, list the rest in the reply for the next run.

## 4. Reply

Post one reply on the lessons issue: today's date (`Lessons run YYYY-MM-DD`), each incident with its change as a link (PR, issue, or "owner" / "orchestrator config" / "skipped: reason"). The next run starts from this date, so it skips what is already answered.

## Nightly

runner-01 cron queues `agent-queue add lessons-YYYYMMDD` with a prompt that says "follow automaton-lessons-learned".
