---
name: pr-review
description: Use when reviewing a pull request, your own or another agent's (Claude, Codex or a person), and before merging any PR.
---

# PR review

Every PR gets an independent review before merge, docs-only PRs too. The reviewer is a fresh agent with no stake in
the change. What to give it and look for is in
[the implement-issue review checklist](../automaton-implement-issue/review.md).

## Post the result where the author will see it

Post the outcome as a formal GitHub review, not a conversation comment. Agents and merge checks read the review
state, and a plain comment is easy to miss:

```bash
gh pr review <N> --request-changes --body-file review.md   # findings above low severity
gh pr review <N> --comment --body-file review.md           # only low findings
gh pr review <N> --approve --body "Independent review: no findings above low."
```

GitHub does not let you approve or request changes on your own PR. Use `--comment` there and start the body with the
verdict (`CHANGES NEEDED` or `APPROVE`).

Rank findings high, medium, low. Give each one a file, a line and a concrete failure case. Say what the reviewer did
not check, for example "the scripts were not run".

## Before merging

- Read **all** reviews and conversation comments on the PR, including ones that arrived after your own review
  started. One that is newer than the last commit, or one that requests changes, blocks the merge until it is
  answered.
- The reviewer checks the tests and the diff before merge. The `proof` artifact comes after merge: release, update
  the test server, verify as real users, then attach it (implement-issue steps 7-8). A failing proof means fix
  forward with a follow-up PR (revert only if the fix is not quick).
- The checks on the final commit pass, and the last review found nothing above low.
- If a PR was merged with open findings, say so on the PR, fix them in a follow-up PR with its own review, and hold
  any release that would ship the problem until the follow-up is merged.
