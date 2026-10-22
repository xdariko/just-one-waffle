# Веб-сборка (WebGL → GitHub Pages)

Workflow `.github/workflows/web-build.yml` на каждый пуш в `main` собирает игру под WebGL
через [GameCI](https://game.ci) и выкладывает на GitHub Pages:
`https://xdariko.github.io/just-one-waffle/`.

## Однократная настройка

1. **Лицензия Unity (Personal).** В Unity Hub: Preferences → Licenses → Add → «Get a free personal license».
   Появится файл `C:\ProgramData\Unity\Unity_lic.ulf` (если его нет — Hub выдал только
   `UnityEntitlementLicense.xml`; тогда см. https://game.ci/docs/github/activation).
2. **Секреты репозитория** (Settings → Secrets and variables → Actions → New repository secret):
   - `UNITY_LICENSE` — всё содержимое `Unity_lic.ulf`;
   - `UNITY_EMAIL`, `UNITY_PASSWORD` — логин и пароль Unity-аккаунта.
3. **Pages.** Settings → Pages → Source: **GitHub Actions**.
   Для приватного репозитория Pages доступны только на платном плане.

После этого — пуш в `main` или Actions → Web build → Run workflow.
Первая сборка идёт долго (импорт всего проекта, ~20–40 мин), дальше быстрее за счёт кэша `Library`.

## Почему так настроено

- **Сжатие Gzip + Decompression Fallback** (Player Settings → Web → Publishing Settings).
  GitHub Pages не умеет отдавать заголовок `Content-Encoding` для готовых `.br`/`.gz`,
  поэтому со сжатием Brotli без fallback игра в браузере не запускается.
  С fallback загрузчик Unity сам распаковывает файлы в браузере.
- Кнопка «Выход» в главном меню на вебе скрыта (`MainMenuController`).
- Сохранение пишется в `persistentDataPath`, в браузере это IndexedDB:
  прогресс живёт в конкретном браузере и пропадает при очистке данных сайта.

## Собрать локально

Нужен модуль **Web Build Support** в Unity Hub (Installs → ⚙ → Add modules).
File → Build Profiles → Web → Build. Открывать `index.html` двойным кликом нельзя —
только через локальный сервер, например `npx serve <папка сборки>`.
