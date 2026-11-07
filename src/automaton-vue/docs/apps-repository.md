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

`catalog init` writes an example app (`apps/hello`, a static page), `catalog-apps.json`, the `Catalog` GitHub workflow and a README, and refuses to overwrite existing files. The workflow runs on each push to `main`: it builds `catalog.json` with `automaton catalog build` and commits it to `main` (it installs the Automaton CLI from NuGet, so it needs a CLI release that has `automaton catalog`: Automaton Server 0.2.36 or newer). Wait for the run to finish (Actions tab) so that `catalog.json` exists in `main`.

A system administrator then opens **Apps → Catalog → Catalog sources** in Studio and adds `https://github.com/<you>/my-apps`. The form labels every field (repository address, optional branch and name, read token) with help text; leave the branch empty for the default branch. The server reads `catalog.json` before saving the source, says why if it cannot, and tells you how many apps the source added. For a private repository, add a fine-grained, read-only token with Contents access to that one repository: the server stores it encrypted, uses it only to read the catalog and to download apps the catalog lists, and never shows it again. The source's apps then appear in the Catalog tab with the source's name; select **Install** to install one. See [Deploy and publish an app](./deploy).

Only add sources you trust: an app is code that runs on your Runners.

## Manage sources from the terminal

A system administrator (or an AI agent working for one) can manage sources with the CLI after `automaton login`:

```bash
automaton catalog sources list
automaton catalog sources add https://github.com/acme/apps --dry-run      # check only, adds nothing
automaton catalog sources add https://github.com/acme/apps --name "Acme apps"
automaton catalog sources add https://github.com/acme/apps --private --token-file ~/catalog-token
automaton catalog sources disable <id>      # also: enable, remove
```

Add `--json` for machine-readable output. The token of a private repository is read only from `--token-file`, `--token-stdin` or a hidden prompt (`--private` without a file), never from an argument or the environment, and it is never printed. **The token must come from you, not from an AI assistant**: create a fine-grained, read-only Contents token for that one repository, save it to a file only you can read, and run the command yourself. When an agent runs `add` for a repository that turns out to be private, the command stops, adds nothing and prints the exact command to run. `--dry-run` checks the address, branch, `catalog.json` and token and prints what would be added.

These commands call `api/admin/catalog/sources`. They accept a system administrator's login or the administrator's own CLI key (the Developer scope `automaton login` creates, or a full-access key); a key of any other user, or a read-only, execute, runner or web-app key, gets 403.

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

## Large catalogs

The Catalog tab shows apps as cards, 24 to a page. Once the catalogs together list more than 12 apps it adds a search box (name, summary and tags), category chips, a source dropdown and an **Installed** switch; the address keeps the filters, so a filtered view can be bookmarked. The server does the filtering and paging from its saved copy of the catalogs, so `GET /api/Catalog` takes `q`, `category`, `source`, `installed`, `page` and `pageSize` (at most 100) and returns the page plus `total`, `totalApps` and the `categories` and `sources` with their counts. A `catalog.json` entry may also carry `tags` (a list of words the search matches) and `icon`; servers ignore fields they do not know.

## The app manifest

Each folder has an `automaton.json` ([reference](./automaton-json)). The catalog takes the app's `name`, its `summary` (one line) and `application.permissions` (what the app asks for) from it, so keep them accurate. Run `automaton validate` inside the app folder: the `applicationContract` check must pass (the link checks apply only once you deploy the app from that folder). Installing an app creates the installer's own copy; permissions are never granted by the catalog and need a system administrator's approval.

## Package an open-source project

An app folder can package a well-known open-source project without changing its source. Keep three things in the folder: `automaton.json`, a `README.md` that names the upstream project and link, its license, the packaged version and the configuration, and the upstream license file. Pin one upstream version in either of two ways. The [official catalog](https://github.com/rzvdaniel/automaton-apps) has examples of both.

**B, from source or a release (the default).** `application.build.command` runs a script of the folder (for example `sh build.sh`) on the Runner. The script downloads one pinned release (a tag, never a branch), checks its SHA-256 against a value written in the script, and builds it, or unpacks the project's own ready-built release archive when compiling needs more memory than a 4 GB Runner has. Set `build.image` to an image with the build tools (pin its version), `build.outputDirectory` to the folder the script fills, and give the runtime the output folder as `workingDirectory`. Examples: `analytics/goatcounter` in the official catalog compiles its Go release in about 90 seconds; [`utilities/it-tools`](https://github.com/rzvdaniel/automaton-apps/tree/main/utilities/it-tools) is a static site that unpacks its release archive.

**A, the project's own image.** `application.runtime.image` points at the image the project publishes, pinned to a version (never `latest`), and the app has no real build. Use it when a build is too heavy for a small Runner, and say so in the app's README. Example: `bookmarks/linkding` in the official catalog.

What to know about the container, for both approaches:

- Build and runtime containers run without Linux capabilities. An image that starts as root and then switches user or changes file owners (`uid =` in uWSGI, `chown` in an entrypoint) must be started with its own command instead of the image's entrypoint.
- The runtime container's root filesystem is read-only. Only `AUTOMATON_APP_DATA_DIR` (and a small `/tmp`) is writable, so copy what must change there at start. The build container's `/tmp` is only 32 MB: build inside the app folder.
- Set `application.runtime.user` to `1000:1000` when the app writes files into its data directory, so the Runner (also uid 1000) can delete them when the app is deleted.
- Studio's install from a catalog or Git address does not accept an app that has a runtime command but no build command yet, so an image-only package carries a no-op build command (`sh -c true`) for now.
- Requests reach the app with the Runner's address as `Host`, and a form post (`application/x-www-form-urlencoded`) loses its body. Apps that need the public host name, or HTML forms, do not work until the platform passes them; say so in the README.
- Environment variables that start with `AUTOMATON_` are reserved. Names a project reserves for itself (GoatCounter's `GOATCOUNTER_`) need other names for your settings.

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

The build fails, listing every problem, when the repository is unknown, a folder has no `automaton.json` or lies at the repository root (put the app in a subfolder), a name, summary or category is empty, `minServerVersion` is not `1.2.3`, two folders share a name, or a folder has no commit yet.

## Protected `main`

`catalog.json` lives in `main`. The workflow from `catalog init` updates it through a pull request, because a protected branch refuses a direct push (and GitHub does not let the Actions identity bypass a personal repository's ruleset). It pushes `catalog.json` to a `catalog-bot/update` branch, opens a pull request labelled `catalog-bot` and squash-merges it. In the repository settings, turn on *Allow GitHub Actions to create and approve pull requests*; the workflow already has `pull-requests: write`. The [automaton-apps workflow](https://github.com/rzvdaniel/automaton-apps/blob/main/.github/workflows/catalog.yml) is a working example.

## Older servers

The official catalog was read from a `catalog` branch of [automaton-apps](https://github.com/rzvdaniel/automaton-apps) on Automaton Server 0.2.32 to 0.2.34; servers from 0.2.35 read `main`. Custom sources need a server with catalog sources (0.2.34 or newer).
