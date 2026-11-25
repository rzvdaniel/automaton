---
name: automaton-release-recap
description: Use after a release reaches production (or when the owner asks "what's in this release?") to publish a visual recap artifact the owner uses to walk and check the release themselves.
license: Apache-2.0
---

# Release recap

One Claude artifact per release: the owner opens it after the deploy and clicks through every change.

## Collect

- PRs: `git log --oneline server-v<prev>..server-v<new>`, take the `(#n)` numbers; for each PR read the title, linked
  issue and the **Check after release** line (`gh pr view <n> --json title,body`).
- Versions and health of each environment (`/health`), website rebuilt or not, Runner upgraded or not.
- Anything not checked yet (release check, verified-partial, owner-only steps).

## Page

Plain HTML artifact (Artifact tool), title `Release <version>`, one file per release in the scratchpad, `icon: "release"`.

1. **Header:** version, date, where it runs (chips per environment: version + healthy), one-sentence summary.
2. **Groups by what the owner sees** (Studio, AI and MCP, Docs, Agents and tooling), not by PR order. Each change is a
   card: plain-language title (what a user can now do), PR and issue links, risk tier when high.
3. **Try it:** the PR's check list rewritten as 1-4 short steps with real links (production URLs,
   `studio.automaton.monster`, `automaton.monster/docs/...`) and commands to copy. Each step has a checkbox kept in
   `localStorage` (per viewer convenience only). Nothing to check → say "Nothing to check".
4. **Open items:** what is not done yet (website rebuild, release check, follow-ups) with links.

Write copy from the user's side: say what changed for them, not class names. Keep it short; no PR body dumps.

## After

Send the owner the link in chat with one line per group. Update the same artifact (same file path) if the state changes
in the session (e.g. website rebuilt).
