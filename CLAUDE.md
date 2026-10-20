@AGENTS.md

Claude Code specifics, on top of the shared instructions above:

- `/deep-planning` enters docs-only planning mode. While it is active, write only under `docs/`, never code, until the owner gives an explicit go-ahead.
- Use the Agent tool for read-only fan-out (surveys, dead-code sweeps, reviews) and verify any claim it makes against the source before writing it into a doc or acting on it.
- Commits end with the attribution line the session provides. Do not commit or push unless the owner says to; they decide when a branch and PR are opened.
- Prefer the Bash tool for file reads and searches in this repo; PowerShell 5.1 is the host shell, so no `&&` in PowerShell commands.
