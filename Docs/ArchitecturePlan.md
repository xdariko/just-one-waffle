# План улучшения архитектуры (перед этапом арта, музыки и UI)

Статусы: ⬜ не начато · 🔄 в работе · ✅ готово и проверено

Ревью от 2026-10-06, ветка `feature/waffle-day-loop`, ~11 тыс. строк C# (из них ~2.6 тыс. — editor-инструменты).

---

## 1. Что уже хорошо (не трогаем)

| Решение | Где | Почему это правильно |
|---|---|---|
| Логика отделена от MonoBehaviour | `OrderEvaluator`, `RecipeScoring`, `ShopService`, `OrderPayout`, `ShiftLedger`, `WaffleOrder`, `GameStats` | Чистые классы, их можно прогнать в batchmode без сцен (`DayRunner.LoadScenes = false`) |
| Данные — в ScriptableObject | `DaySettings`, `EconomySettings`, `ShopCatalog`, `WaffleRecipe`, `StoryVisit`, `DecorStationLayout`… | Баланс и контент правятся без кода |
| Переходы дня проверяют фазу | `DayRunner.StartDay/AcceptOrder/DeliverWaffle/...` | Двойное нажатие и перезагрузка сцены ничего не повторяют, это работает как конечный автомат |
| Деньги переводятся один раз | `ShiftLedger` с ключами + `GameProgress.CommitShift` | Нет дюпов и двойных начислений |
| Надёжное сохранение | `GameProgress`: tmp → replace, `.bak`, `.corrupted`, миграции по версии | Готово к релизу |
| Факт отдельно от оценки | `CookingSession` хранит сырые значения, оценка считается потом | Можно менять правила оценки без изменений в мини-играх |
| Контент проверяется инструментом | `Tools → Waffle → Проверить контент` | Ловит битые ссылки до Play |

Вывод: **ядро игры (домен) спроектировано хорошо**. Почти все проблемы ниже — в слое представления: UI, визуал, звук и связь сцен. Именно этот слой сейчас будет меняться, поэтому его нужно подготовить **до** того, как пойдут спрайты и UI.

---

## 2. Главные проблемы

### 2.1. UI полностью собирается кодом (блокирует «классный UI»)
`CounterController.BuildPanel`, `ShopPanel.Build/AddCard`, `DayHud.Build`, `CookingUI.CreateButton` создают каждый Image и Label через `new GameObject`, с позициями, цветами и размерами шрифтов прямо в коде. Чтобы сделать красивый UI, придётся править C#, а не префаб. Кроме того, логика завязана на текст кнопки (`label == "Магазин"`).

### 2.2. Нет звука и нет точки, куда его подключать
Ни одного `AudioSource` в коде. Мини-игры ничего не сообщают наружу («яйцо разбилось идеально», «вафля сгорела»): результат только записывается в `CookingSession` и выводится в `Debug.Log`.

### 2.3. Переходы между сценами — жёсткая склейка
`SceneManager.LoadScene` вызывается в двух местах (`DayRunner.GoToKitchen`, `PackingStation`) без затемнения и асинхронной загрузки. Имя сцены стойки хранится в двух местах (`DaySettings.counterScene` и `PackingStation.dialogueSceneName`). Под музыку и затемнение нужна одна точка перехода.

### 2.4. Сервисы живут в разных сценах
`DayRunner` лежит в `SampleScene`, `CookingManager` — в `MainScene`, оба с `DontDestroyOnLoad`. `ShopCatalog.Active` выставляется из двух мест (`DayRunner`, `DecorStation` как запасной вариант). Если запустить кухню отдельно, `DayRunner` в ней нет. С появлением `AudioService` и `SceneLoader` будет ещё два таких объекта.

### 2.5. Визуал зашит в логику
- `WaffleIronBake`: цвета пропечки (`ColorFor`) и пар (`PixelSprite.Create` + `Destroy` каждые 0.2 с) находятся внутри логики таймера.
- `DecorStation` (561 строка): одновременно раскладка ячеек, рисование соусом, перетаскивание вафли, упаковка и подписи мест.
- `sortingOrder` задаётся числами в 12 местах (`waffleSortingOrder + 50`, `100`, `30`…), sorting layers нигде не используются. Когда появятся настоящие спрайты, порядок отрисовки начнёт ломаться.

### 2.6. Мини-игры обращаются к глобальному состоянию напрямую
`CookingManager.Instance.Current` используется в 12 местах. Правило «одно действие за раз» собрано из трёх механизмов: `PourItem.Active`, `WhiskItem.Active`, `FindAnyObjectByType<EggSwipeDownItem>()` в `WhiskClick`. Добавить новую мини-игру (миксер, начинка) — значит добавить ещё одну такую проверку во все места.

### 2.7. Дублирование кода перетаскивания
Прокрутка у края экрана, `GetPointerWorld()` и анимация возврата на место скопированы в `BowlCarry`, `PlatedWaffle` и `DecorStation`.

### 2.8. Мелочи
- `DayRunner` (638 строк): в одном классе машина состояний дня, генерация заказа и реплик (`CreateRegularVisit`, `PickLines`, `PhrasingsFor`) и расписание сюжетных визитов.
- Около 100 вызовов `Debug.Log` с интерполяцией строк: в релизной сборке на телефоне это аллокации и лишний вывод.
- Аллокации каждый кадр: `CookingSession.IronProduct` (LINQ) опрашивается из `Update` в `WaffleIronBake` и `BowlCarry`; `DayHud.Refresh` каждый кадр собирает строку; `GestureLock.IsActive` создаёт лямбду.
- `DecorWaffle.Paint` вызывает `SetPixels32` + `Apply` для всей текстуры на каждый кадр мазка.
- Нет `.asmdef`: любая правка перекомпилирует весь проект.
- Тестовые хуки в рантайм-коде (`ClickPanelButtonForTest`, `PackSlotForTest`…) попадают в сборку.
- Мусор: `Square.prefab.orig`, неиспользуемый `WaffleIronBake.platePoint`, режим `BuildLegacy` в `DecorStation`, старый `PortionSourceClick` для сахара, названия сцен `SampleScene`/`MainScene`.

---

## 3. Решения

Принципы: не переписывать домен; не добавлять фреймворки ради фреймворков (без DI-контейнеров и без ECS — для пошаговой 2D-игры с одним клиентом за раз это лишнее); **ввод на кухне остаётся на старом `Input` + `OnMouse*`** (отдельный слой указателя уже ломал жесты — не повторяем).

### Р1. UI на префабах: View + Presenter
- Каждый экран — префаб с компонентом-View, у которого `[SerializeField]` ссылки на Text, Button, Image. Код только **заполняет** View данными и подписывается на кнопки.
  - `CounterPanelView` (заголовок, текст, ряд кнопок), `ShopView` + `ShopCardView` (префаб карточки), `DayHudView`, `DialogueView` (уже частично: `DialogueManager` берёт ссылки из инспектора).
- Логика остаётся там, где есть: `CounterController` решает, что показать; `ShopService` — можно ли купить.
- Кнопки — по ролям, а не по тексту: `ShowPanel(title, body, PanelButton.Shop, PanelButton.StartDay)`.
- Стили — `UiTheme` (ScriptableObject): цвета, шрифты TMP, спрайты кнопок. Цвета из `ShopPanel` (`BoxColor`, `Ink`…) и `CounterController` переносятся туда.
- `CookingUI.Create*` остаётся только в editor-скрипте, который один раз генерирует префабы-заглушки (как сейчас `WaffleShopSetup`).

### Р2. Игровые события → звук и эффекты
Один статический хаб без ScriptableObject-каналов, потому что проект уже использует статику (`GestureLock`, `ShopCatalog.Active`) и так проще:

```csharp
public static class KitchenEvents
{
    public static event Action<EggCrackResult, Vector3> EggCracked;
    public static event Action<PourIngredient> PourStarted, PourStopped;
    public static event Action<MixResult> MixDone;
    public static event Action BakeStarted;
    public static event Action<BakeResult> WaffleTakenOut;
    public static event Action<DecorIngredient, Vector3> ToppingPlaced;
    public static event Action WafflePacked;
    // + Raise-методы; DayRunner.Changed уже есть для фаз дня
}
```
Мини-игры только **сообщают** о событии (одна строка там, где сейчас `Debug.Log` с результатом). На события подписываются `AudioService`, эффекты (частицы, тряска камеры, всплывашки) и будущая аналитика. Мини-игры при этом ничего не знают о звуке.

### Р3. AudioService + SoundBank
- `SoundBank` (ScriptableObject): `enum SoundId` → `AudioClip[]` (случайный вариант), громкость, разброс pitch.
- `AudioService` (постоянный объект): пул из 8 `AudioSource` для SFX и 2 источника для музыки с кроссфейдом. Музыка выбирается по `DayPhase` / сцене (стойка, кухня, итоги, магазин).
- `AudioMixer` с группами Music / SFX / UI, громкость каждой хранится в `PlayerPrefs` (это настройка устройства, а не прогресс, поэтому не в `progress.json`).
- Импорт: SFX — `Decompress On Load`, музыка — `Streaming`, Vorbis.

### Р4. Bootstrap и SceneLoader
- Префаб `Systems` (в `Resources/`), который создаётся до первой сцены через `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`: `DayRunner`, `CookingManager`, `AudioService`, `SceneLoader`. Любую сцену можно запустить через Play, сервисы есть всегда, порядок `Awake` не важен.
- `SceneLoader.Go(GameScene.Kitchen)`: затемнение → `LoadSceneAsync` → снятие затемнения. `enum GameScene` вместо строк; имена сцен — в одном месте.
- Переименовать сцены: `SampleScene` → `Counter`, `MainScene` → `Kitchen`.
- `ShopCatalog.Active` выставляет только bootstrap.

### Р5. Кухня: один «замок действия» и общий перенос
- `KitchenAction` вместо `PourItem.Active` / `WhiskItem.Active` / `FindAnyObjectByType<EggSwipeDownItem>`:
  `KitchenAction.TryBegin(owner)`, `End(owner)`, `IsBusy`. Новая мини-игра получает правило «одно действие за раз» бесплатно.
- `CarryHelper` — **статические функции**, а не слой ввода: `ScreenToWorld(cam)`, `EdgeScroll(scroller, zone, speed)`, корутина `ReturnTo(transform, pos, scale, time)`. Ввод по-прежнему читается внутри `BowlCarry`, `PlatedWaffle` и `DecorStation` через `Input`.
- Мини-игры получают сессию через `CookingManager.Instance.Current` в одном месте (свойство `Session` в базовом классе `KitchenTool : MonoBehaviour`), а не в каждом методе.

### Р6. Отделить визуал от логики станций
- `WaffleIronBake` → логика + `WaffleIronView` (крышка, пар, цвет вафли, подсветка). Пар — `ParticleSystem` (поле уже предусмотрено для пакетов), без Instantiate/Destroy.
- `DecorStation` → `DecorStationBuilder` (раскладка, ячейки, места), `DecorPainter` (соус и добавки по выбранной вафле), `DecorStation` (приём вафли, перенос, упаковка).
- Цвета пропечки → `WaffleLook` (ScriptableObject): спрайт или цвет на каждый `BakeResult`. Тот же ассет использует `DecorStation`.
- **Sorting layers**: `Background`, `Table`, `Stations`, `Items`, `Held` (то, что в пальце), `FX`, `WorldUI`. Магические `sortingOrder` заменить на слой + небольшой порядок внутри слоя.

### Р7. DayRunner: вынести генерацию визитов
- `VisitFactory` (чистый класс): `CreateRegular`, `CreateFallback`, `CreateStory`, `PickLines`, `PhrasingsFor`.
- `StoryScheduler`: списки `pending` / `scheduled` / `dayEnd`, `Tick`, `Next`.
- `DayRunner` остаётся тонкой машиной состояний (фазы, таймер, журнал, события). Так он сократится примерно до 250 строк. Отдельные классы-состояния (State pattern) не нужны: пять фаз с проверками в методах читаются лучше.

### Р8. Производительность и релизная гигиена (мобильные)
- `Log.Info/Warn` с `[Conditional("WAFFLE_LOG")]` (символ включён в редакторе, выключен в релизе) вместо `Debug.Log`. `LogError` оставить как есть.
- `CookingSession`: хранить `IronProduct` / `PlateProduct` в полях, которые обновляются при смене состояния, а не вычислять LINQ на каждый запрос.
- `DayHud`: обновлять текст раз в секунду или по `Changed`.
- `GestureLock`: чистить мёртвых владельцев в `End` / при загрузке сцены, а не в геттере.
- `DecorWaffle.Paint`: один `Apply()` за кадр, а не за каждый отрезок; позже — рисование соуса через `RenderTexture` + материал-кисть.
- Спрайты: Sprite Atlas v2 по сценам (Counter, Kitchen, UI), одинаковый PPU, сжатие ASTC.
- `Application.targetFrameRate = 60` в bootstrap (на Android по умолчанию 30).
- Тестовые хуки — под `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

### Р9. Структура кода
- `.asmdef`: `Waffle.Runtime`, `Waffle.Editor` (ссылается на Runtime и Graph Toolkit), `Waffle.DialogueGraph.Editor`.
- Неймспейсы `Waffle.Day`, `Waffle.Cooking`, `Waffle.Shop`… — по желанию, при переходе на asmdef.
- Удалить мусор из п. 2.8.

---

## 4. Порядок работ

Сначала то, что разблокирует арт, звук и UI. Остальное делается по ходу.

| # | Этап | Решение | Зачем именно сейчас | Объём | Статус |
|---|---|---|---|---|---|
| 1 | Bootstrap + SceneLoader с затемнением, переименование сцен | Р4 | Нужен для музыки и переходов; убирает ошибку «нет DayRunner» | S | ✅ |
| 2 | Sorting layers + `WaffleLook` | Р6 (часть) | До того, как положить первый настоящий спрайт | S | ✅ |
| 3 | `KitchenEvents` + `AudioService` + `SoundBank` | Р2, Р3 | Звук можно подключать, не трогая мини-игры | M | ⬜ |
| 4 | UI на префабах: `UiTheme`, `CounterPanelView`, `DayHudView`, `ShopView`/`ShopCardView` | Р1 | Главное условие для красивого UI | L | ⬜ |
| 5 | Разделить `WaffleIronBake` и `DecorStation` на логику и вид, пар на частицах | Р6 | Анимации и арт станций | M | ⬜ |
| 6 | `KitchenAction` + `CarryHelper` | Р5 | Перед новыми мини-играми (миксер, начинки) | S | ⬜ |
| 7 | `VisitFactory` + `StoryScheduler` | Р7 | Перед расширением сюжета и типов клиентов | M | ⬜ |
| 8 | Логи, аллокации, атласы, `targetFrameRate`, asmdef, чистка | Р8, Р9 | Перед первой сборкой на телефон | S | ⬜ |

S — до дня, M — 1–2 дня, L — 3+ дня.

**Проверка каждого этапа** — как и раньше, без тест-файлов: компиляция и прогон дня в batchmode (временный editor-скрипт, удалить после), `Tools → Waffle → Проверить контент` без ошибок, ручная проверка жестов кухни в Editor (в batchmode живой ввод не проверить). После этапа 8 — Unity Profiler на устройстве: кадр ≤ 16 мс, нет GC-аллокаций в кадре на кухне.

---

## 5. Чего сознательно не делаем (и что берём вместо)

| Подход | Вердикт | Почему | Когда пересмотреть |
|---|---|---|---|
| **Новый Input System на кухне / общий слой указателя** | ✗ | Уже ломал жесты: отставание на кадр против `OnMouse*` | — |
| **DI-контейнер** (Zenject, VContainer) | ✗ сейчас | Постоянных сервисов три-четыре, их создаёт префаб `Systems` (этап 1). Главный выигрыш DI — подмена зависимостей в unit-тестах, а тест-файлов в проекте нет: проверка идёт прогонами. Контейнер добавил бы «магию» (кто кого создал) и время на изучение | Сервисов станет 8+ или появятся две реализации одного сервиса (например, локальное и облачное сохранение) |
| **ScriptableObject event channels** | ✗ | Статический `KitchenEvents` проще и прозрачнее при одном разработчике | Если события понадобится связывать в инспекторе без кода |
| **Addressables** | ✗ сейчас | Две сцены и 2D-атласы — обычных ссылок хватает; Addressables добавляют сборку бандлов, асинхронную загрузку каждого ассета и отдельный шаг в билде. `Resources/` используем ровно для одного префаба `Systems` — **арт туда не класть**: всё из `Resources` попадает в сборку и индексируется при старте | (1) сборка > ~150 МБ; (2) захочется подгружать **новые рецепты и ингредиенты с сайта без обновления приложения** — это как раз удалённый каталог Addressables |
| **ECS / DOTS** | ✗ | Нужен для тысяч одинаковых сущностей (толпы, пули, симуляции). Здесь одновременно живут десятки объектов, узкое место — не CPU. Переписывание ради ECS ничего не даст игроку | — |
| **Пул объектов везде** | ✗ везде, ✓ точечно | Пул окупается, только когда объект создаётся и уничтожается часто. Таких мест три: пар вафельницы (новый объект каждые 0.2 с), всплывашки `LuckPopup`, частицы струй. Ячейки, вафли и добавки создаются по одной за действие игрока — пул там только усложнит код | — |

**Что внедряем вместо:**
- **`UnityEngine.Pool.ObjectPool<T>`** (встроенный в Unity, свой класс не пишем) — для `LuckPopup` и других всплывашек (этап 5).
- **`ParticleSystem` вместо самодельного пара** — он сам переиспользует частицы, отдельный пул не нужен (этап 5).
- **DOTween** — для этапа 4 (UI) и анимаций станций. Сейчас каждая анимация — своя корутина с `Lerp`, а для «сочного» UI нужны easing, последовательности (`Sequence`) и отмена (`SetLink`/`Kill`). Пакет импортируется на этапе 4.
