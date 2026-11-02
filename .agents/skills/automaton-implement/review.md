# Review

Spawn a fresh, read-only subagent with no stake in the change. Give it:

- the issue number and done criteria;
- the PR number or the branch diff against `main`;
- the area skills that apply;
- what to look for: correctness, authorization (who can do what), data loss, missing tests for the new behaviour,
  needless code (helpers, options or layers the task does not need), and docs that are missing or too long.

Ask for findings ranked by severity (high, medium, low), each with the file, line and a concrete failure case.

Fix every confirmed finding above low. Take product questions to the owner. Review once more after fixing. After two
rounds, stop: list anything left in the PR and the handoff instead of looping.
