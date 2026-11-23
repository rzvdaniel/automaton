# Automaton theme

One look for Studio, the website and every own app. Open `theme.html` (next to this file) in a browser for the
swatches, components and dark mode. The same page is kept in automaton, automaton-apps and automaton-monster; change
all three together.

## Colours

| Studio token (`tokens.css`) | App UI kit (`app.css`) | Light | Dark | Use |
|---|---|---|---|---|
| `--paper` | `--surface` | `#fdfdfb` | `#121316` | cards, inputs |
| `--panel`, `--app-bg` | `--bg` | `#f3f2ee` | `#1b1c20` | page background, insets |
| `--ink` | `--text` | `#17181c` | `#f1f0eb` | text, bars for "fine" |
| `--ink-soft` | `--muted` | `#55565e` | `#a9aab1` | secondary text, "fine" dots |
| `--line` | `--border` | `#e2e0da` | `#2c2d33` | borders, empty bars |
| `--signal` | `--accent` | `#f26a1b` | `#ff7a2e` | needs you, in progress, primary button |
| `--signal-ink` | `--orange-ink` | `#a23d05` | `#ffa36b` | orange text |
| `--signal-soft` (not yet a token) | `--orange-soft` | `#ffe6d6` | `#2e1b10` | needs-you strips and pills |
| `--bad` (not yet a token) | `--danger` | `#b42318` | `#f87171` | failed, destructive |
| `--rail` | `--rail` | `#17181c` | `#0b0c0e` | sidebar, app headers |

Fonts: Bricolage Grotesque (headings), Instrument Sans (body), IBM Plex Mono (versions, ids, logs).

Studio still defines `--ok` (green); it is legacy, do not use it for new UI.

## Status

| State | Look | Examples |
|---|---|---|
| Fine | grey dot and words, dark bar | up to date, passed, online |
| Needs you | soft orange pill, orange bar | update available, waiting for approval |
| In progress | orange outline, not a fill | updating, deploying |
| No data yet | line-coloured dot or empty bar | not checked yet |
| Failed or destructive | red, the only place for red | update failed, delete |

- Never green, never blue, including for success and information.
- Status is a pill or bar with words, never colour alone; no coloured left borders or tinted whole cards.
- One orange primary button per view. Destructive buttons are outlined red and always confirm.
- While refreshing or updating, keep the last values and mark them; show one error line with the reason and a retry,
  never a stack of error boxes.

## The mark

Orange tile with a dark `>_<` (`resources/logo/` in automaton), 32 px beside the app name in every header. The dark
tile with orange strokes is for places where orange is already busy.
