---
title: Publish your own app catalog
description: The format of an apps repository (catalog.json, catalog-apps.json, app folders) and how to create one with automaton catalog init and add it as a catalog source.
---

# Publish your own app catalog

An apps repository is a GitHub repository whose apps an Automaton server can list under **Apps → Catalog** and install. Create one with the CLI, push it, then add it as a catalog source.

## Create and publish one

```bash
automaton catalog init my-apps     # writes files only: it never runs git or calls GitHub
cd my-apps
git init -b main && git add . && git commit -m "First apps repository"
# create the GitHub repository, then:
git remote add origin https://github.com/<you>/my-apps.git && git push -u origin main
```

`catalog init` writes an example app (`apps/hello`, a static page), `catalog-apps.json`, the `Catalog` GitHub workflow and a README, and refuses to overwrite existing files. The workflow runs on each push to `main`: it builds `catalog.json` with `automaton catalog build` and commits it to `main`. Wait for the run to finish (Actions tab) so that `catalog.json` exists in `main`.

A system administrator then opens **Apps → Catalog → Catalog sources** in Studio and adds `https://github.com/<you>/my-apps`. The server reads `catalog.json` before saving the source and says why if it cannot. For a private repository, add a fine-grained, read-only token with Contents access to that one repository: the server stores it encrypted, uses it only to read the catalog and to download apps the catalog lists, and never shows it again. The source's apps then appear in the Catalog tab with the source's name; select **Install** to install one. See [Deploy and publish an app](./deploy).

Only add sources you trust: an app is code that runs on your Runners.

## Layout

```text
my-apps/
  catalog.json            generated; the index a server reads (root of main)
  catalog-apps.json       the list of apps you want in the catalog
  apps/
    hello/
      automaton.json      the app's manifest (name and summary are required here)
      ...                 the app's files
  .github/workflows/catalog.yml
```

App folders can sit anywhere in the repository. The folder name (its last part) is the app's id in the catalog.

## catalog-apps.json

You edit this file. Each entry names an app folder:

```json
{
  "repo": "owner/name",
  "apps": [
    { "folder": "apps/hello", "category": "Examples", "minServerVersion": "0.2.32" }
  ]
}
```

- `folder` (required): path of the app folder from the repository root; letters, digits, `.`, `_`, `-` and `/`, no `.` or `..` parts, at most 300 characters.
- `category` (required): a short label shown on the entry.
- `minServerVersion` (required): the oldest Automaton Server that can run the app, as `1.2.3`. Servers older than that show the entry but do not offer Install.
- `repo` (optional): `owner/name`. When it is missing the build uses `--repo` or the `GITHUB_REPOSITORY` variable that GitHub Actions sets.

## The app manifest

Each folder has an `automaton.json` ([reference](./automaton-json)). The catalog takes the app's `name`, its `summary` (one line) and `application.permissions` (what the app asks for) from it, so keep them accurate. Run `automaton validate` inside the app folder: the `applicationContract` check must pass (the link checks apply only once you deploy the app from that folder). Installing an app creates the installer's own copy; permissions are never granted by the catalog and need a system administrator's approval.

## catalog.json

Generated, never edited by hand. Servers read `catalog.json` from the root of the repository's `main` branch (a source can name another branch).

```json
{
  "schemaVersion": 1,
  "repo": "owner/name",
  "apps": [
    {
      "id": "hello",
      "name": "Hello",
      "summary": "A tiny static page that shows your catalog works.",
      "category": "Examples",
      "folder": "apps/hello",
      "commit": "<40-character commit SHA>",
      "minServerVersion": "0.2.32",
      "permissions": []
    }
  ]
}
```

- `schemaVersion` is `1`. A server refuses any other number and keeps the last list it read.
- `commit` pins the app: it is the last commit that changed the app's folder, so an install fetches exactly the files that were listed, and an entry never points at the commit that updates `catalog.json`. Commit your app before building.
- A server installs an entry only from the repository of the source it came from, whatever `repo` says.
- An invalid entry is skipped; the other entries still show. A file larger than 1 MiB is refused.

## Build it yourself

```bash
automaton catalog build            # writes catalog.json in the current folder
automaton catalog build --validate # only checks the entries
automaton catalog build --repo owner/name --out /tmp/catalog.json
```

The build fails, listing every problem, when a folder has no `automaton.json`, a name or summary is empty, `minServerVersion` is not `1.2.3`, two folders share a name, or a folder has no commit yet.

## When `main` requires pull requests

The workflow from `catalog init` commits `catalog.json` straight to `main`, which a protected branch refuses. Instead let CI open a pull request and merge it itself: in the repository settings allow GitHub Actions to create pull requests, give the workflow `pull-requests: write`, push `catalog.json` to a `catalog-bot/update` branch, then run `gh pr create` and `gh pr merge --squash`. The [automaton-apps workflow](https://github.com/rzvdaniel/automaton-apps/blob/main/.github/workflows/catalog.yml) is a working example.

## Older servers

The official catalog was read from a `catalog` branch of [automaton-apps](https://github.com/rzvdaniel/automaton-apps) on Automaton Server 0.2.32 to 0.2.34; servers from 0.2.35 read `main`. Custom sources need a server with catalog sources (0.2.34 or newer).
