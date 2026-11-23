# Automaton theme

One look for Studio, the website and every own app. Open `theme.html` (next to this file) for swatches and
components. The same two files are kept in automaton (`docs/design/`), automaton-apps and automaton-monster
(`design/`); change all three together.

## Colours

The values live in code: Studio `src/automaton-studio-vue/src/assets/styles/tokens.css`, apps
`design/automaton-app-ui/app.css` (the Automaton look). Dark values are there too. Light values:

| Studio token | App UI kit | Light | Use |
|---|---|---|---|
| `--paper` | `--surface` | `#fdfdfb` | cards, inputs |
| `--panel` | `--bg` | `#f3f2ee` | page background, insets |
| `--ink` | `--text` | `#17181c` | text, bars for "fine" |
| `--ink-soft` | `--muted` | `#55565e` | secondary text, "fine" dots |
| `--line` | `--border` | `#e2e0da` | borders, empty bars |
| `--signal` | `--accent` | `#f26a1b` | needs you, failed, in progress, primary button |
| `--signal-ink` | `--orange-ink` | `#a23d05` | orange text |
| none yet | `--orange-soft` | `#ffe6d6` | needs-you strips and pills |
| none yet | `--danger` | `#b42318` | destructive actions only |
| `--rail` | `--rail` | `#17181c` | sidebar, app headers |

Studio still defines a green `--ok`; it is legacy, do not use it for new UI. Fonts: Studio and the website use
Bricolage Grotesque, Instrument Sans and IBM Plex Mono; apps keep the UI kit's own fonts.

## Status

| State | Look | Examples |
|---|---|---|
| Fine | grey dot and words, dark bar | up to date, passed, online |
| Needs you, failed | soft orange pill, orange bar | update available, update failed, waiting for approval |
| In progress | orange outline, not a fill | updating, deploying |
| No data yet | line-coloured dot or empty bar | not checked yet |
| Destructive | red, the only place for red | delete, data loss |

- Never green, never blue, including for success and information.
- Status is a pill or bar with words, never colour alone; no coloured left borders or tinted whole cards.
- One orange primary button per view. Destructive buttons are outlined red and always confirm.
- While refreshing or updating, keep the last values and mark them; show one line with the reason and a retry, never
  a stack of error boxes.

## The mark

Orange tile with a dark `>_<` (`resources/logo/` in automaton), 32 px beside the app name in every header. The dark
tile with orange strokes is for places where orange is already busy.
