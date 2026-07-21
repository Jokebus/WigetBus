# CLAUDE.md

## O projektu
WigetBus — C# WPF desktopový widget (hodiny/kalendář, svátky, státní svátky CZ).
GitHub: https://github.com/Jokebus/WigetBus (private), branch `main`.

## Workflow
- Před větší úpravou zkontroluj `git status` — projekt se otevírá i ve Visual Studiu, mezi běhy tam mohou být změny mimo Claude Code.
- Po dokončení smysluplného kroku udělej commit s krátkou výstižnou zprávou.
- Necommituj rozdělanou/nefunkční práci, pokud to nemá důvod.
- Po commitu rovnou pushni na GitHub (`git push`).
- Nikdy neuprav ani nemaž `.gitignore` bez upozornění uživatele.
- Pokud push selže kvůli přihlášení/tokenu, zastav se a nahlas to — neřeš to obcházením.

## Poznámky
- `bin/`, `obj/`, `dist/`, `packages/`, `.vs/`, `.vscode/` jsou v `.gitignore` — build výstupy negenerovat do repa.
