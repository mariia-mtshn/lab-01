# Звіт до лабораторної роботи № 1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Робоча гілка: `lab/1-system`; основна гілка: `main`.
- Фінальний тег: `v0.1.0`.
- Commit hash перевіреного стану: `⟦ВСТАВ результат git rev-parse HEAD⟧`.
- .NET SDK: `⟦ВСТАВ результат dotnet --version⟧`.

## 2. Стан PostgreSQL

Вивід `docker compose --env-file infra/.env.example -f infra/compose.yaml ps` (стан `healthy`):

```text
⟦ВСТАВ короткий вивід docker compose ps⟧
```

Запит `GET /health` повернув `200 OK`, тіло `{"status":"ready"}`.

## 3. Змінений маршрут

```text
кнопка «Показати підсумок» → loadSeveritySummary (Client/app.js)
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync
  → IncidentQueries.GetSeveritySummaryAsync
      (AsNoTracking, GroupBy, Count, ToListAsync)
  → SecureLabDbContext.Incidents / таблиця incidents
  → IncidentSeveritySummaryResponse як JSON
  → textContent у списку підсумку
```

Детальна карта, межі довіри й конфігураційні входи описані в `docs/architecture.md`.

## 4. Що реалізовано

- Замість baseline 501 endpoint повертає 200 і JSON-масив елементів `{severity, count}`.
- Окремий response DTO `IncidentSeveritySummaryResponse` без зайвих полів entity.
- Політика нульових груп: лише наявні групи. Порядок сталий: Critical → High → Medium → Low.
- Необов'язковий параметр `?status=` з явним allowlist; невідоме значення дає 400 Validation Problem Details.
- Клієнт: кнопка, вибір статусу, стани «Завантаження…», «Даних немає» і безпечне фіксоване повідомлення про помилку; DOM тільки через `textContent`.
- Структуроване журналювання з `TraceId` без чутливих даних.
- Автоматичні тести й `.http`-сценарії (`tests/http/incidents.http`).

## 5. Результати перевірки

| ID | Дія | Очікувано | Фактично |
|---|---|---|---|
| T-01 | `GET /health` | 200 | 200 OK, `{"status":"ready"}` |
| T-02 | `GET /api/incidents?status=Triaged` | 200, відфільтрований список | ⟦ВСТАВ: status і що повернулось⟧ |
| T-03 | `GET /api/incidents?status=Resolved` | 200, `[]` | ⟦ВСТАВ: status і тіло⟧ |
| T-04 | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details | 404 Not Found, `application/problem+json`, title «Інцидент не знайдено» |
| T-05 | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | ⟦ВСТАВ: status і Content-Type⟧ |
| T-06 | `GET /api/incidents/severity-summary` | 200; High, Medium, Low по 1; без Critical; порядок за критичністю | 200 OK, `application/json; charset=utf-8`, `[{"severity":"High","count":1},{"severity":"Medium","count":1},{"severity":"Low","count":1}]` |
| T-07 | натиснути «Показати підсумок» у клієнті | список у DOM; стани завантаження, порожньо, помилка | Список показано (`Груп: 3`; High 1, Medium 1, Low 1). ⟦ВСТАВ: що бачила для станів завантаження / «Даних немає» / помилки⟧ |
| T-08 | reset seed, повторити T-02 і T-06 | той самий результат | ⟦ВСТАВ: результат після reset⟧ |
| T-09 | `GET .../severity-summary?status=Triaged` | 200, `[{"severity":"Medium","count":1}]` | 200 OK, `[{"severity":"Medium","count":1}]` |
| T-10 | `GET .../severity-summary?status=Resolved` | 200, `[]` | 200 OK, `[]` |
| T-11 | `GET .../severity-summary?status=Unknown` | 400 | 400 Bad Request, `application/problem+json` |
| T-12 | `GET .../severity-summary?status=1` | 400 | 400 Bad Request, `application/problem+json` |

## 6. Знахідки в starter

**Знахідка 1: слабка валідація `status`.**
До виправлення `GET /api/incidents?status=1` повертав 200 і список: `Enum.TryParse` перетворював число на значення enum, а `Enum.IsDefined` його пропускав. Так само проходили значення на кшталт `New,Triaged`. Причина: перевірка спиралась на парсинг enum, а не на явний перелік допустимих значень (`IncidentEndpoints.GetListAsync`). Виправлення: allowlist назв статусів у `IncidentEndpoints.TryParseStatus`, спільний для list і summary. Після виправлення: `?status=1` → 400, `?status=New,Triaged` → 400 (обидва `application/problem+json`). Регресійні тести: `GetList_WithInvalidStatus_Returns400`, `GetSeveritySummary_WithInvalidStatus_Returns400`.

**Знахідка 2: невідомий API-маршрут повертав HTML.**
До виправлення `GET /api/does-not-exist` повертав 200 з `Content-Type: text/html` (запасний маршрут `MapFallbackToFile("index.html")`), тож клієнт API не міг відрізнити помилку маршруту від успіху. Виправлення: маршрут `/api/{**path}` у `Program.cs` перед запасним маршрутом. Після виправлення: 404 з `application/problem+json`. Регресійний тест: `UnknownApiRoute_ReturnsProblemDetails404_NotHtmlFallback`.

## 7. Автоматична перевірка

Команда: `powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1` (піднімає PostgreSQL, збирає проєкт, відновлює seed, запускає `dotnet test`).

Фактичний результат: `⟦ВСТАВ рядок Test summary: total: …; failed: …; succeeded: …⟧`.

Перед дописаними тестами baseline мав 4 тести; нові тести покривають summary (порядок, поля, фільтр, порожній результат, некоректні значення) і обидві знахідки.

## 8. Перегляд diff

Перед кожним commit переглянуто `git status`, `git diff` і `git diff --staged`. Секретів, `.env`, cookies, токенів, дампів БД і журналів у staged diff немає. Відкриті навчальні облікові дані локального стенда (`securelab` / `local-study-password`) залишилися лише в `infra/compose.yaml` і `appsettings.Development.json`, як передбачає baseline.

## 9. Висновок

Endpoint `GET /api/incidents/severity-summary` реалізовано за контрактом: 200 із сталим порядком груп, окремий DTO без зайвих полів, allowlist для `status`, безпечний вивід у DOM. У ході роботи виявлено й виправлено дві проблеми starter (слабка валідація `status` і HTML-відповідь для невідомого `/api/...`), для кожної додано регресійні тести. Стенд відтворюється через seed/reset, результат зафіксовано в тегу `v0.1.0`.