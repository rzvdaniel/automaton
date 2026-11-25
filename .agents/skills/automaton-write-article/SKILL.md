---
name: automaton-write-article
description: Use when planning, drafting or producing an Automaton article (LinkedIn post, blog post on blog.automaton.monster) that showcases a feature, from idea to published post.
license: Apache-2.0
---

# Writing an Automaton article

Articles live on board 7 (Articles), one issue per article. The owner edits and publishes; the agent plans, measures,
captures and drafts.

## Shape

- **Length:** 500-600 words. Short paragraphs, no jargon a non-developer would stumble on.
- **One real story:** something we actually did (our blog is Ghost on Automaton, our logs are in VictoriaLogs). Never
  invent users, quotes or numbers.
- **One measured number** near the end (minutes, clicks, euros, files), measured on the test server for this article.
- **One link** at the end: `https://automaton.monster` or the matching docs page.
- **Structure:** hook (2 lines) → the usual pain (1 paragraph) → what Automaton does differently → the steps (3 at
  most, each with a screenshot) → a second short example → the number → the link.
- Only strong examples: if an app or screen does not look good, leave it out.

## Pictures

| Picture | How |
|---|---|
| One diagram of the idea | Automaton Whiteboard design system artifact (whiteboard sketch), at most ~6 boxes |
| Studio screenshots | Real, from the test server; no personal data, no `e2e-` names, light mode unless the point is dark mode |
| Live result | Real, from production (e.g. the blog, an app page) |
| Code or config | A short excerpt (10 lines or fewer) as a clean screenshot, never a whole file |
| Cover | The diagram, or the key screen with one element highlighted |

Save them with the draft (`draft.md`) in a folder outside the repo (the session scratchpad), named `<nn>-<what>.png`. Articles are never committed.

## Steps

1. **Issue:** reopen or create the article issue on board 7 with the plan (story, structure, pictures, the number to
   measure). Follow `automaton-write-issue` for its shape.
2. **Measure:** do the thing on the test server the way a reader would, and time it. Note the version.
3. **Capture** the screenshots and draw the diagram.
4. **Draft** `draft.md` in plain words; mark places where the owner's voice or opinion is needed with `TODO(owner)`.
5. **Hand off:** give the owner the folder path (draft and pictures); the owner keeps their own copy, edits, publishes on
   LinkedIn and on the blog, and may bring the final version back for a review.

## Don't

- Publish, post or send anything yourself; the owner does.
- Promise features that are not live on production.
- Copy text from other articles or the upstream projects' sites; link them instead.
