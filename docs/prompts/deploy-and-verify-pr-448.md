# Deploy and verify PR #448

Prompt for a coding agent with access to the production server and the GitHub
repository. [PR #448](https://github.com/rzvdaniel/automaton/pull/448) changes
the API and Studio (server release), the CLI (NuGet release) and the website
docs (separate deploy). It does not change the Runner, so no Runner release is
needed.

```text
Deploy and verify merged PR #448 in rzvdaniel/automaton (merge commit dff9af24).
Read `gh pr view 448`, AGENTS.md and .agents/skills/production-deployment/SKILL.md first.
Live deployment is explicitly requested.

1. Server: publish a server bundle from main via .github/workflows/server-release.yml,
   then update the production server with its update.sh, following the skill
   (backups, migrations check, no fresh installer). Verify API, Studio, routing and
   the Runner broker connection.
2. CLI: publish a new Automaton.Cli NuGet version via .github/workflows/nuget.yml
   (see docs/prod/cli-nuget-publishing.md), then `dotnet tool update -g Automaton.Cli`.
3. Website: find how automaton.monster is deployed; ask me before deploying it.

Acceptance (report each as verified / failed / not checked):
- `automaton --help` runs on a machine with only .NET 10, without DOTNET_ROLL_FORWARD.
- In an empty folder: `automaton new "PR448 check"`, add a trivial app, then
  `automaton deploy` with no set-runner → output shows a runnerAssignment check
  "assigned to the only available Runner …" and the deploy succeeds.
  Delete this test app afterwards.
- `automaton logs --json` returns a URL with `?view=logs&target=`; it opens the Logs view.
- Studio login page has no "Remember me"; sidebar icon buttons have aria-labels;
  /cli/authorize shows the title "Authorize CLI"; admin pages show their own titles.
- After the server update, `automaton sync-ai` guidance contains the WebSocket/streaming
  limitation and no "automaton target approve".
- The Untranslatable game at https://untranslatable.apps.automaton.monster/ still works.
Do not paste or log secrets. Report the released versions and anything not verified.
```

A server update briefly interrupts the API and Studio. Schedule it outside
times when hosted apps are in active use.
