# After launch

Updated: 4 November 2026. Status: planned. Work and status: parent issue [#627](https://github.com/rzvdaniel/automaton/issues/627) and its sub-issues; this file holds the design.

Once Automaton is public, most first contact happens through documentation, the website, the README and Studio, and more and more often through an AI agent acting for a person. This plan makes each of them simpler for people **and** agents, and lets an administrator or agent update a server without SSH.

## Rules

- **One source per topic.** Other places link to it. Generated agent guidance comes from the same source where possible.
- **Written for agents too:** short, task-shaped pages, exact commands and flags, expected output and a way to verify. `llms.txt` is the entry point for agents.
- **Claims come with numbers** from real runs (#561). Nothing outdated stays published.
- **Studio's redesign** follows the website's visual style, shared as one design system, with stable labels and test ids. Every Studio action is also available in the CLI and API.
- **The API never runs as root.** Updates from Studio or the CLI go through a root-owned trigger that runs the existing updater with its checks and recovery backup.

## Plan

Steps 1–3 can run in parallel; step 3 links to step 1's pages. Step 4 starts with an audit and a design that needs the owner's approval. Step 5 is independent.

1. **Documentation** (#628). Task pages with one source per topic (install, first administrator, Runner, deploy, update, backup and restore, secrets), `/llms.txt` and `/llms-full.txt` generated from the docs at website build, `scripts/check-doc-links.mjs` in CI, historical architecture plans archived. Implemented in the PR for #628 (pending review). Not yet done: a measured install-to-app timing page (#629) and an end-to-end run by an agent given only `llms.txt`.
2. **Website** (#629). Remove outdated content, simplify, and show the measured install-to-app path.
3. **README** (#630). About one screen, quickstart with exact commands, links to the docs. Implemented in the PR for #630 (pending review).
4. **Studio** (#631). Audit, simpler information architecture, redesign matching the website, agent-friendly.
5. **Update from Studio and the CLI** (#632). Root path unit triggered by the API, `automaton server update --wait`, an Update button in Studio. Implemented in the PR for #632 (pending owner approval and a real-server test); automatic patch updates are a follow-up.
