---
title: Publish an App
description: Make your Automaton app public. Publish a deployed app to share it with anyone using automaton publish.
---

# Publish

`automaton publish` makes the latest deployed version publicly accessible.

```bash
automaton publish
```

## Recommended flow

1. Build the app locally
2. Run `automaton deploy`
3. Ask whether you want it public yet
4. Run `automaton publish`

## Why publish is separate

Deploy is the technical sync step.

Publish is the visibility step.

That means you can deploy safely without immediately exposing the app.

## Typical AI prompt

```text
Build a poker planning app for Automaton Monster.
Deploy with automaton deploy.
Ask me before publish.
```

## Useful options

```bash
automaton publish --slug poker-planning
automaton publish --yes --json
```

Use `--yes` for non-interactive automation after approval has already been given.

## Marketplace listing

A published app is listed in the public marketplace by default. Set how it
appears there from the CLI:

```bash
automaton listing set --summary "A bluffing game about real words" --category Games --tags "game,multiplayer"
automaton listing set --description "Everyone invents a fake meaning, then votes for the true one."
automaton listing cover cover.png
automaton listing add-screenshot lobby.png reveal.png
automaton listing show
```

Options you leave out of `listing set` stay unchanged; `--category ""` clears
the category. The summary is up to 200 characters and the description up to
500. Images are PNG, JPG, WEBP or GIF up to 2 MB; an app has one cover and up
to 8 screenshots. Remove a screenshot with
`automaton listing remove-screenshot <id>`, using an ID from `listing show`.
The same settings are in Studio under the app's Settings.
