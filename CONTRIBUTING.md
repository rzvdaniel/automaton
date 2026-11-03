# Contributing to Automaton

Thanks for your interest. Automaton is in early access and maintained by a small team, so small, focused changes are easiest to review.

## Where to go

- **Questions and ideas:** [GitHub Discussions](https://github.com/rzvdaniel/automaton/discussions).
- **Bugs and feature requests:** [open an issue](https://github.com/rzvdaniel/automaton/issues/new/choose) from a template.
- **Security problems:** never in public; see [SECURITY.md](SECURITY.md).
- **What is planned:** the [roadmap](docs/roadmap/README.md) and the [project board](https://github.com/users/rzvdaniel/projects/1).

## Build and test

Set up a local development environment with [repository development](docs/development.md) (.NET 9 SDK, Node.js 22+, Docker). The checks CI runs on every pull request:

```bash
dotnet build Automaton.All.sln -c Debug
dotnet test src/UnitTests/Automaton.Api.Tests        # also Cli, Core, Runner, Worker, Ai, Architecture
cd src/automaton-vue && npm ci                         # Studio tests import website config
cd ../automaton-studio-vue && npm ci && npm test && npx vue-tsc --noEmit -p tsconfig.app.json
git diff --check
```

Run the tests for the projects you changed. Add a test for new behaviour and, for a bug, a test that fails before the fix.

## Propose a change

1. For anything larger than a small fix, open or comment on an issue first so we can agree on the approach.
2. Branch from `main` and keep the change focused on one issue; do not reformat unrelated code.
3. Open a pull request that says `Closes #<issue>`, what changed and why, which docs you updated, and which tests you ran.
4. Pull requests are squash-merged once CI passes and a review finds nothing blocking.

Update the documentation in the same pull request when behaviour people see changes. Never commit credentials, tokens, `.env` files or local databases.

## Coding agents

Coding agents (and people working with them) follow [AGENTS.md](AGENTS.md): the repository rules, keep-it-simple and maintainability guidelines, and the area skills under `.agents/skills/` it lists for each part of the code. `CLAUDE.md` only points agents to `AGENTS.md`.

By contributing you agree that your contributions are licensed under the [Apache 2.0 license](LICENSE).
