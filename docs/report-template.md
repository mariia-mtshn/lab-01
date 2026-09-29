# Звіт до лабораторної роботи № 1

## 1. Ідентифікація стану
- Варіант: 2-A «Трекер інцидентів».
- Робоча гілка: `lab/1-system`; основна: `main`.
- Фінальний тег: `v0.1.0`.
- Commit hash: `cf29a5f183e998597cd1f365522e4ffa6f51ab08`.
- .NET SDK: `10.0.300`.
- PostgreSQL запускається через Docker Compose.
- API доступний на `http://localhost:5080`

Під час виконання лабораторної роботи зміни вносилися в окремій гілці `lab/1-system`. Локальний файл `infra/.env` не відстежується Git відповідно до правила `.gitignore`.

## 2. Стан PostgreSQL

PostgreSQL запускається командою:

```text
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
```

Після запуску контейнер PostgreSQL переходить у стан `healthy`.

Readiness endpoint:

```text
GET /health
```

повертає:

```json
{"status":"ready"}
```

з HTTP status `200 OK`.

Це підтверджує, що API успішно працює та може взаємодіяти з PostgreSQL.

## 3. Змінений маршрут

Наскрізний маршрут реалізованого функціоналу:

```text
кнопка «Показати підсумок»
    ↓
loadSeveritySummary() у Client/app.js
    ↓
GET /api/incidents/severity-summary[?status=...]
    ↓
IncidentEndpoints.GetSeveritySummaryAsync()
    ↓
IncidentQueries.GetSeveritySummaryAsync()
    ↓
AsNoTracking()
    ↓
GroupBy(Severity)
    ↓
Count()
    ↓
ToListAsync()
    ↓
PostgreSQL / таблиця incidents
    ↓
IncidentSeveritySummaryResponse
    ↓
JSON response
    ↓
textContent у browser client
```

## 4. Що реалізовано

Реалізовано endpoint:

```text
GET /api/incidents/severity-summary
```

який повертає кількість інцидентів за рівнем severity.

Приклад відповіді:

```json
[
  {"severity":"High","count":1},
  {"severity":"Medium","count":1},
  {"severity":"Low","count":1}
]
```

Реалізовано:

- окремий response DTO `IncidentSeveritySummaryResponse`;
- групування інцидентів за `Severity`;
- підрахунок кількості через `Count()`;
- `AsNoTracking()` для read-only запиту;
- необов'язковий параметр `status`;
- allowlist допустимих статусів;
- HTTP `400 Validation ProblemDetails` для некоректного status;
- сталий порядок severity: `Critical → High → Medium → Low`;
- політику «лише наявні групи»;
- browser client із кнопкою та фільтром статусу;
- стани «Завантаження…», «Даних немає» та повідомлення про помилку;
- безпечне виведення результатів через `textContent`;
- структуроване журналювання з `TraceId`.

## 5. Результати перевірки

| ID | Дія | Очікувано | Фактично |
|---|---|---|---|
| T-01 | `GET /health` | 200 | `200 OK`, `{"status":"ready"}` |
| T-02 | `GET /api/incidents?status=Triaged` | 200, один інцидент | `200 OK`, повернуто один інцидент зі статусом `Triaged` |
| T-03 | `GET /api/incidents?status=Resolved` | 200, `[]` | `200 OK`, порожній JSON-масив `[]` |
| T-04 | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details | `404 Not Found`, `application/problem+json`, title «Інцидент не знайдено» |
| T-05 | `GET /api/incidents?status=Unknown` | 400 Validation ProblemDetails | `400 Bad Request`, `application/problem+json` |
| T-06 | `GET /api/incidents/severity-summary` | 200; High, Medium, Low по 1 | `200 OK`, `High:1`, `Medium:1`, `Low:1` |
| T-07 | Фільтр статусу у browser client | Дані змінюються відповідно до фільтра | Усі → 3 групи; New → Low:1; Triaged → Medium:1; InProgress → High:1; Resolved/Closed → «Даних немає» |
| T-08 | Reset seed та повторна перевірка | Той самий результат | Reset виконано, після reset summary знову повертає 3 групи |
| T-09 | `GET /api/incidents/severity-summary?status=Triaged` | 200, `Medium:1` | `200 OK`, `[{"severity":"Medium","count":1}]` |
| T-10 | `GET /api/incidents/severity-summary?status=Resolved` | 200, `[]` | `200 OK`, `[]` |
| T-11 | `GET /api/incidents/severity-summary?status=Unknown` | 400 | `400 Bad Request`, `application/problem+json` |
| T-12 | `GET /api/incidents/severity-summary?status=1` | 400 | `400 Bad Request`, `application/problem+json` |

У browser client також перевірено стани завантаження та помилки API. При вимкненому API відображається фіксоване повідомлення:

```text
Не вдалося завантажити підсумок. Спробуйте пізніше.
```

## 6. Знахідки в starter

### Знахідка 1: слабка валідація `status`

До виправлення:

```text
GET /api/incidents?status=1
```

міг повертати `200`, оскільки попередня перевірка використовувала парсинг enum.

Також могли проходити значення, які не є одним допустимим статусом, наприклад:

```text
New,Triaged
```

Проблема полягала в тому, що перевірка спиралася на парсинг enum, а не на явний перелік дозволених назв.

Виправлення виконано в `IncidentEndpoints.TryParseStatus()` за допомогою allowlist:

```text
New
Triaged
InProgress
Resolved
Closed
```

Після виправлення:

```text
?status=1
```

та

```text
?status=New,Triaged
```

повертають `400 Bad Request`.

Додано регресійні тести для list та summary endpoint.

### Знахідка 2: невідомий API-маршрут повертав HTML

До виправлення:

```text
GET /api/does-not-exist
```

міг потрапити на SPA fallback і повернути `index.html` із `200 OK`.

Це некоректно для API, оскільки клієнт очікує HTTP-помилку, а не HTML-сторінку.

У `Program.cs` додано окрему обробку:

```text
/api/{**path}
```

перед SPA fallback.

Після виправлення невідомий API-маршрут повертає:

```text
404 Not Found
Content-Type: application/problem+json
```

Для цієї поведінки також додано регресійний тест.

## 7. Автоматична перевірка

Для автоматичної перевірки використовується:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

Скрипт:

1. запускає PostgreSQL через Docker Compose;
2. збирає API;
3. виконує reset seed;
4. запускає автоматичні тести.

Фактичний результат:

```text
Test summary: total: 15, failed: 0, succeeded: 15, skipped: 0
```

Таким чином, усі 15 автоматичних тестів пройшли успішно.

Baseline містив 4 тести. Додані тести перевіряють:

- endpoint severity summary;
- порядок груп;
- поля response;
- фільтрацію за status;
- порожній результат;
- некоректні значення status;
- невідомий API-маршрут;
- відсутність небезпечного `innerHTML`.

## 8. Перегляд diff та робота з конфігурацією

Перед commit перевірялися:

```text
git status
git diff
git diff --staged
```

Локальний файл:

```text
infra/.env
```

не відстежується Git.

У репозиторії використовується:

```text
infra/.env.example
```

як приклад необхідних змінних середовища.

## 9. Git

Робота виконувалася у гілці:

```text
lab/1-system
```

Зміни були розділені на окремі змістовні commits.


## 10. Висновок

У ході лабораторної роботи було розгорнуто та перевірено вебсистему, що складається з browser client, ASP.NET Core Web API та PostgreSQL у Docker Compose.

Реалізовано endpoint:

```text
GET /api/incidents/severity-summary
```

який виконує групування інцидентів за severity та повертає кількість записів у кожній наявній групі.

Для реалізації використано `AsNoTracking`, `GroupBy`, `Count` і `ToListAsync`. Результат повертається через окремий response DTO. Додано необов'язковий параметр `status` із серверною allowlist-валідацією та обробкою некоректних значень через `400 Validation ProblemDetails`.

У browser client реалізовано відображення результату, стани завантаження та порожнього результату, а також безпечне додавання тексту до DOM через `textContent`.

Під час дослідження starter було виявлено та виправлено дві проблеми: слабку валідацію параметра `status` та повернення HTML для невідомих API-маршрутів через SPA fallback. Для виправлень додано регресійні тести.

Фінальна автоматична перевірка завершилася результатом:

```text
15 tests passed, 0 failed, 0 skipped.
```
