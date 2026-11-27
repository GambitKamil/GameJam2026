# Подключение к проекту — порядок для ИИ-агента

Этот файл выполняет **ИИ-агент** (Codex, Claude Code и т.п.) вместе с человеком. Человек может быть новичком в Git и Unity.

**Как работать по этому файлу:**
- Говори с человеком **по-русски**, коротко, по одному шагу.
- Каждый шаг: проверь → если не так, исправь сам или скажи человеку, что сделать руками → переходи дальше, только когда шаг пройден.
- Пароли, одноразовые коды и вход в браузере делает **человек**. Ты их не вводишь и не просишь прислать.
- Если твоя песочница не пускает в сеть (`git clone`, `git push`, `gh`), попроси человека разрешить команду.
- В конце выведи таблицу «шаг — ✅/❌» и список того, что человеку осталось сделать руками.

**Константы проекта:**

| | |
|---|---|
| Репозиторий | https://github.com/GambitKamil/GameJam2026 |
| Ветка | `main` (все работают в ней) |
| Unity | **6000.3.29f1**, ревизия `96d2fe17529c` |
| Сборка | **Windows** (обязательно), браузерная — по желанию |
| Роли | `design-ui`, `gameplay`, `systems` (см. `docs/roles/`) |

---

## 1. Инструменты

- `git --version`. Нет git → macOS: `xcode-select --install` (или `brew install git`); Windows: `winget install Git.Git`.
- `gh --version` (GitHub CLI). Нет → macOS: `brew install gh`; Windows: `winget install GitHub.cli`.

## 2. Вход в GitHub

- `gh auth status`. Если не вошёл — запусти `gh auth login --hostname github.com --git-protocol https --web`. Команда покажет одноразовый код и ссылку https://github.com/login/device. Скажи человеку: открыть ссылку, войти в **свой** аккаунт, ввести код, нажать **Authorize**.
- Затем `gh auth setup-git` — git будет отправлять изменения через этот вход, без паролей.

## 3. Доступ на запись

- `gh api repos/GambitKamil/GameJam2026 --jq .permissions.push` → должно быть `true`.
- `false` → владелец репозитория (GambitKamil) добавляет человека: **Settings → Collaborators → Add people**. Человек принимает приглашение: https://github.com/GambitKamil/GameJam2026/invitations (или письмо на почте). Потом проверь снова.

## 4. Клонирование

- Если текущая папка уже этот проект (`git remote get-url origin` указывает на `GameJam2026`) — сделай `git pull` и переходи к шагу 5.
- Иначе: `git clone https://github.com/GambitKamil/GameJam2026.git` и дальше работай внутри папки `GameJam2026`.
- **Нельзя** класть проект в iCloud Drive, OneDrive, Dropbox, Google Drive и другие синхронизируемые папки — они ломают Unity. Лучше путь без пробелов и кириллицы, например `~/Projects/GameJam2026` или `C:\Projects\GameJam2026`.

## 5. Настройки git (только для этого репозитория)

```
git config pull.rebase true
git config rebase.autoStash true
git config user.name  "<логин GitHub>"
git config user.email "<id>+<логин>@users.noreply.github.com"
```

Логин и id: `gh api user --jq '.login, .id'`. Почта вида `…@users.noreply.github.com` — скрытая почта GitHub: настоящая не попадёт в публичную историю.

Первые две строки делают `git pull` безопасным: свои незакоммиченные правки git временно откладывает, подтягивает чужие коммиты и кладёт твои сверху — без лишних merge-коммитов.

## 6. Unity той же версии

Все должны работать в **одной и той же** версии: другая (даже соседняя) переписывает файлы проекта и даёт конфликты.

- Требуемая версия — в `ProjectSettings/ProjectVersion.txt` (`6000.3.29f1`).
- Найди установленный редактор. Обычные места:
  - macOS: `/Applications/Unity/Hub/Editor/6000.3.29f1/Unity.app`
  - Windows: `C:\Program Files\Unity\Hub\Editor\6000.3.29f1\Editor\Unity.exe`
  - Не нашёл — спроси человека: **Unity Hub → Installs** — есть ли там 6000.3.29f1 и где лежит.
- **Нет нужной версии** → человек открывает в браузере ссылку `unityhub://6000.3.29f1/96d2fe17529c` — Unity Hub предложит поставить именно её.
- **Модуль сборки под Windows** нужен тому, кто собирает игру (роль `systems`), и желателен остальным — чтобы самим проверять сборку:
  - на **Windows** ничего ставить не нужно — сборка под Windows встроена;
  - на **macOS** нужен модуль **Windows Build Support (Mono)**. Есть, если существует папка `…/6000.3.29f1/PlaybackEngines/WindowsStandaloneSupport`. Нет → Unity Hub → Installs → ⚙ у 6000.3.29f1 → **Add modules** → Windows Build Support (Mono).

## 7. Первое открытие в Unity

- Человек: **Unity Hub → Projects → Add → Add project from disk** → папка репозитория → открыть в **6000.3.29f1**. Первое открытие долгое (Unity строит папку `Library/`).
- Проверь `git status`: файл `ProjectSettings/ProjectVersion.txt` **не изменён**. Изменён → проект открыли не той версией: закрыть Unity, `git checkout -- ProjectSettings Packages`, вернуться к шагу 6.
- Ошибок компиляции нет: в конце лога Unity нет строк `error CS`.
  - macOS: `~/Library/Logs/Unity/Editor.log`
  - Windows: `%LOCALAPPDATA%\Unity\Editor\Editor.log`
  - Или человек смотрит окно **Console** в Unity — красных строк быть не должно.
- **По желанию — MCP for Unity** (агент сможет сам управлять редактором): в Unity **Window → MCP for Unity** → Transport **Stdio** → **Start Session**; ниже Client → свой агент (Codex / Claude Code) → **Configure**; перезапустить агента.

## 8. Роль на этой машине

Создай в корне репозитория файл `.agent-role`:

```
<роль>
<имя человека латиницей>
```

Роль — одно слово: `design-ui`, `gameplay` или `systems`. Файл в `.gitignore` — он только для этой машины. По нему агент в каждой следующей сессии сам узнаёт роль (так написано в `AGENTS.md`).

## 9. Проверочный push

Проверяем, что изменения доходят до команды.

1. Человек в Unity: **File → New Scene** (Basic) → **File → Save As** → `Assets/_Game/Scenes/Sandbox/<Name>.unity` (имя латиницей). Это его песочница — пробовать там что угодно.
2. Ты:
   ```
   git add Assets/_Game/Scenes/Sandbox/<Name>.unity Assets/_Game/Scenes/Sandbox/<Name>.unity.meta
   git commit -m "<роль>: sandbox scene for <Name>"
   git pull
   git push
   ```
3. Проверка: `git log origin/main --oneline -3` — твой коммит там. Напарники получат его своим `git pull`.

## 10. Итог

Покажи таблицу шагов ✅/❌. Затем прочитай `AGENTS.md`, `README.md` и `docs/roles/<роль>.md` и коротко расскажи человеку: его зона, первые задачи, как синхронизироваться с командой.
