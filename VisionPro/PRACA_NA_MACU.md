# Praca nad projektem bezpośrednio na Macu mini (nowa rozmowa z Claude Code)

Ten plik prowadzi krok po kroku: od zera do nowej rozmowy z Claude, który widzi i zmienia pliki projektu
na Twoim komputerze, a potem do zbudowania aplikacji na iPhonie. Kroki zakładają macOS i Terminal.

Poprzednie rozmowy (w chmurze) nie przenoszą się same: cała wiedza o projekcie jest w repozytorium
(`CLAUDE.md`, `AUDIT_PL.md`, `VisionPro/HANDOVER.md`), a Claude czyta ją na początku każdej rozmowy.

---

## 1. Co jest potrzebne (jednorazowo)

| Narzędzie | Po co | Jak sprawdzić |
|---|---|---|
| **Git** | pobieranie i wysyłanie zmian | `git --version` (jeśli brakuje, macOS zaproponuje „Command Line Tools”) |
| **Claude Code** | rozmowa z Claude na Twoim komputerze | `claude --version` |
| **Unity Hub + Unity 6000.6.8f1** | budowanie projektu (moduł **iOS Build Support**) | Unity Hub > Installs |
| **Xcode** (zalogowany Apple ID) | podpisanie i instalacja na iPhonie | Xcode > Settings > Accounts |
| **iPhone** podłączony kablem, zaufany, z włączonym Trybem programisty | uruchamianie aplikacji | Ustawienia > Prywatność i ochrona > Tryb programisty |

Konto Claude (plan z dostępem do Claude Code) albo klucz API z https://platform.claude.com/settings/keys
przyda się do logowania w kroku 3.

## 2. Pobranie projektu

```bash
mkdir -p ~/"Nowy/Projekty i Kod/VR"
cd ~/"Nowy/Projekty i Kod/VR"
git clone https://github.com/sebastianstein3/Victor_VR.git
cd Victor_VR
```

- Jeśli „Nowy” leży gdzie indziej niż w katalogu domowym, wpisz `cd ` (ze spacją), przeciągnij folder „VR”
  z Findera do okna Terminala i naciśnij Enter, a potem uruchom tylko `git clone` i `cd Victor_VR`.
- Repozytorium prywatne? Zaloguj się: `brew install gh` (jeśli nie masz), `gh auth login`, potem
  `gh repo clone sebastianstein3/Victor_VR`.
- Sprawdź adres zdalny: `git remote -v` ma wskazywać na `Victor_VR` (nie `victor_vr` ze starej nazwy; to ten sam
  projekt, ale GitHub przekierowuje tylko na jakiś czas). Naprawa: `git remote set-url origin https://github.com/sebastianstein3/Victor_VR.git`.

## 3. Instalacja Claude Code

Jeśli `claude --version` już działa, przejdź do kroku 4. W przeciwnym razie wybierz jedną drogę
(aktualne polecenia podaje https://code.claude.com/docs, jeśli któreś nie zadziała):

```bash
curl -fsSL https://claude.ai/install.sh | bash
```

albo przez Homebrew:

```bash
brew install --cask claude-code
```

Potem w folderze projektu uruchom:

```bash
claude
```

Przy pierwszym uruchomieniu wybierz sposób logowania (konto Claude albo klucz API) i zaufaj folderowi projektu.
Klucza nie wklejaj do rozmowy ani do żadnego pliku w repozytorium.

## 4. Pierwsza wiadomość w nowej rozmowie

Claude sam wczyta `CLAUDE.md` z folderu projektu. Wklej mu poniższy tekst (cały):

```text
Kontynuujemy projekt Vision Pro DIY (iPhone 16 Pro + Shinecon G10, silnik CardboardXR, Unity 6000.6.8f1).
Poprzednie rozmowy odbywały się w chmurze, gdzie nie było Unity ani Xcode, więc nic z kodu nie było
kompilowane ani uruchamiane. Teraz pracujesz bezpośrednio na moim Macu mini, który ma Unity i Xcode.

Zacznij od przeczytania CLAUDE.md, AUDIT_PL.md (zwłaszcza sekcje 6a–6d) i VisionPro/HANDOVER.md.
Potem:
1. Otwórz projekt w Unity w trybie wsadowym (albo powiedz mi, jak mam go otworzyć) i zbierz błędy kompilacji.
   Najbardziej podejrzane są pliki z ostatnich zmian: Assets/_Project/Demos/Workspace/Scripts/
   AssistantSession.cs, AgentTools.cs, MiniJson.cs, WorkspaceAgent.cs, HologramAssistant.cs,
   HologramHud.cs, MemoryView.cs oraz wtyczki Assets/Plugins/iOS/CardboardXRFiles.mm, CardboardXRPdf.mm,
   CardboardXRAssistant.mm.
2. Napraw błędy małymi commitami (po polsku lub angielsku) i zaktualizuj AUDIT_PL.md.
3. Dopilnuj plików .meta dla nowych plików (generuje je Unity) i zacommituj je.
4. Dopiero gdy projekt się kompiluje, zbudujemy aplikację na iPhone'a: zsh Tools/ios_build_and_run.sh.
Przed git push upewnij się, że origin wskazuje na Victor_VR. Odpowiadaj po polsku.
```

## 5. Co Claude może teraz zrobić (czego nie mógł w chmurze)

- uruchamiać Unity w trybie wsadowym i czytać prawdziwe błędy kompilacji,
- uruchamiać `Tools/ios_build_and_run.sh` (Unity → Xcode → iPhone) i czytać logi sesji,
- poprawiać kod do skutku zamiast zgadywać.

Przykładowe polecenie Unity w trybie wsadowym (zamknij wcześniej Unity z tym projektem; ścieżka zależy od wersji):

```bash
/Applications/Unity/Hub/Editor/6000.6.8f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -projectPath "$PWD" -logFile - 2>&1 | grep -E "error CS|Error|Exception" | head -50
```

Claude zrobi to sam, jeśli poprosisz: „zbierz błędy kompilacji z Unity”.

## 6. Kolejność prac na Macu (zalecana)

1. **Kompilacja** w Unity (krok 4.1–4.3). Scena `Workspace.unity` powstaje z menu
   **CardboardXR > Make Missing Demo Scenes** w edytorze Unity (to się robi w oknie Unity, nie w Terminalu).
2. **Pakiety opcjonalne:** `com.unity.cloud.gltfast` (Modele 3D) dodaj w Package Managerze po nazwie.
3. **Build na iPhone'a:** `zsh Tools/ios_build_and_run.sh` (pierwszy raz skonfiguruj podpisywanie w Xcode;
   szczegóły w nagłówku skryptu). Przy innym identyfikatorze aplikacji: `CARDBOARDXR_BUNDLE_ID=com.twoja.nazwa`.
4. **Test na urządzeniu**, w tej kolejności: dyktowanie i okna → hologram V.I.C.T.O.R. → klucz („wklej klucz”) →
   Model i Pamięć → research → pliki z połączonego folderu → MCP.
5. Wyniki testów (co działa, co nie, zrzuty błędów) wklej Claude'owi: poprawi i zaktualizuje `AUDIT_PL.md`.

## 7. Reguły, których Claude trzyma się w tym projekcie (z `CLAUDE.md`)

- Bazowe pliki CardboardXR zostają nietknięte; nasze demo żyje w `Assets/_Project/Demos/Workspace/`.
- Nigdy nie commitujemy `Library/`, `Builds/`, kluczy ani tokenów (klucz API trafia do pęku kluczy iOS).
- Commity małe, po polsku lub angielsku; po zmianach krótka aktualizacja `AUDIT_PL.md`.

## 8. Wznawianie i porządek

- Kolejna rozmowa w tym samym folderze: `cd` do projektu i `claude` (albo `claude --continue`, żeby wrócić do
  ostatniej rozmowy z tego folderu, `claude --resume`, żeby wybrać z listy).
- Rozmowa stała się długa i nieporęczna? Zacznij nową: stan prac jest w `AUDIT_PL.md`, więc nic nie ginie.
- Zmiany z chmury pobierzesz przez `git pull`; swoje wyślesz przez `git push` (Claude zrobi to na prośbę).

## 9. Gdy coś nie działa

| Objaw | Co zrobić |
|---|---|
| `command not found: claude` | zamknij i otwórz Terminal; jeśli dalej, powtórz instalację z kroku 3 |
| `git clone` pyta o hasło i odmawia | repozytorium prywatne: `gh auth login` albo token dostępu, nie hasło |
| Unity: „Project is already open” w trybie wsadowym | zamknij okno Unity z tym projektem |
| Brak Unity 6000.6.8f1 | zainstaluj tę wersję w Unity Hub z modułem iOS albo ustaw `UNITY=/ścieżka/do/Unity` dla skryptów |
| Xcode nie podpisuje | Xcode > Settings > Accounts (Apple ID), potem wybierz Team w projekcie `Builds/iOS` |
| `devicectl` pokazuje „connected (no DDI)” | włącz Tryb programisty na iPhonie i uruchom ponownie telefon |
