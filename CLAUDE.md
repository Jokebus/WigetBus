# CLAUDE.md

## O projektu
WigetBus — C# WPF desktopový widget (hodiny/kalendář, svátky, státní svátky CZ).
GitHub: https://github.com/Jokebus/WigetBus (PUBLIC — přísnější kontrola tajemství před commitem), branch `main`.

## Kontext
Osobní AI vrstva: `C:\AI\Ai_OS`
Paměť tohoto projektu: `C:\AI\Ai_OS\memory\projects\wigetbus.md`

## Workflow
- Před větší úpravou zkontroluj `git status` — projekt se otevírá i ve Visual Studiu, mezi běhy tam mohou být změny mimo Claude Code.
- Po dokončení smysluplného kroku udělej commit s krátkou výstižnou zprávou.
- Necommituj rozdělanou/nefunkční práci, pokud to nemá důvod.
- Push NEPROVÁDĚJ sám — po commitu řekni, že je to připravené k odeslání, a počkej na pokyn.
- Nikdy neuprav ani nemaž `.gitignore` bez upozornění uživatele.
- Pokud push selže kvůli přihlášení/tokenu, zastav se a nahlas to — neřeš to obcházením.
- Závazný postup pro commity, verzování a release je skill `git-sync`; při rozporu s tímhle souborem platí skill.

## Poznámky
- `bin/`, `obj/`, `dist/`, `packages/`, `.vs/`, `.vscode/` jsou v `.gitignore` — build výstupy negenerovat do repa.
