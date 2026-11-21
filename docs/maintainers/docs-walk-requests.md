# User requests for the docs walk

Each line is something a user asks an AI agent. The docs walk (#1216) starts with the release check and plays two of them per release, rotating (plus the pages changed since the last release, #1491): a fresh agent reads only `llms.txt` and the docs of the release being walked (the test server's own docs or the docs built from the release tag, not the published website), plays the request on the test server (Windows: on a Windows machine with WSL), and records every guess, every input the docs do not tell it to ask for, and anything it would wrongly ask for (passwords, tokens). Fix the docs from that list. Origin: #1228.

| # | Request | Start page | Played on the test server |
| --- | --- | --- | --- |
| 1 | Install Automaton on my empty server | install-server, first-admin | not yet (needs a fresh server and DNS) |
| 2 | Add a Runner on the Automaton server | deploy-runner | no |
| 3 | Add a Runner on another Linux server over SSH | deploy-runner | 2026-11-16 (#1228) |
| 4 | Add a Runner on my Linux PC | deploy-runner | no |
| 5 | Add a Runner on my Windows PC | deploy-runner (Windows) | not testable on the Linux test server |
| 6 | Build an app and deploy it | build-an-automaton-app, deploy | 2026-11-16 (#1228, static) |
| 7 | Publish my app | deploy | 2026-11-16 (#1228) |
| 8 | Give my app a secret | secrets-and-permissions | no |
| 9 | Update the server | update-server | no |
| 10 | Upgrade a Runner | deploy-runner | 2026-11-16 (#1228, already current) |
| 11 | Remove a Runner | deploy-runner | 2026-11-16 (#1228, manual path) |
| 12 | Back up the server and restore it | backup-restore | no |
| 13 | Install an app from the catalog | deploy (Marketplace) | no |
| 14 | Pause or delete an app | deploy | 2026-11-16 (#1228, delete) |

Update the last column after each walk.
