# Карта архітектури

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | HTTP-запити через `fetch`, безпечний вивід через `textContent` / `createTextNode` |
| ASP.NET Core API | `src/SecureLab.Api/Presentation/`, `Program.cs` | Маршрути, читання параметрів, allowlist `status`, HTTP-відповіді, Problem Details |
| Application layer | `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs` | Read-only запити (`AsNoTracking`), проєкція в response DTO |
| EF Core / Npgsql | `src/SecureLab.Api/Data/SecureLabDbContext.cs` | Відображення `DbSet<Incident>` на таблицю `incidents` |
| PostgreSQL | `infra/compose.yaml` | Дані в локальному контейнері Docker Compose |

## Змінений маршрут: підсумок інцидентів за severity

```text
кнопка «Показати підсумок» у Client/index.html
  → loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.GetSeveritySummaryAsync (allowlist status)
  → IncidentQueries.GetSeveritySummaryAsync
  → SecureLabDbContext.Incidents / таблиця incidents (GROUP BY severity)
  → IncidentSeveritySummaryResponse (severity, count) як JSON
  → renderSeveritySummary: textContent у <li>
```

Ключові файли:

- клієнт: `Client/index.html`, `Client/app.js`;
- endpoint: `Presentation/Endpoints/IncidentEndpoints.cs`;
- application layer: `Application/Incidents/IncidentQueries.cs`;
- DTO: `Presentation/Contracts/IncidentResponses.cs` (`IncidentSeveritySummaryResponse`);
- DbContext: `Data/SecureLabDbContext.cs`; таблиця `incidents`.

Контракт:

- метод і URL: `GET /api/incidents/severity-summary`, тіла запиту немає;
- необов'язковий параметр `?status=` перевіряється за allowlist (`New`, `Triaged`, `InProgress`, `Resolved`, `Closed`); інше значення дає 400 Validation Problem Details;
- політика нульових груп: лише наявні групи (наприклад, `Critical` на baseline seed відсутній);
- порядок сталий: Critical → High → Medium → Low. Сортування в SQL було б лексикографічним, бо `Severity` зберігається як текст, тому порядок задається в коді після матеріалізації агрегату;
- відповідь містить лише поля `severity` і `count`;
- порожній результат (наприклад, `status=Resolved`) дає `200` з `[]`.

## Дослідження базового маршруту: перегляд одного інциденту

```text
натискання картки інциденту
  → loadIncidentDetails у Client/app.js
  → GET /api/incidents/{id}
  → IncidentEndpoints.GetDetailsAsync
  → IncidentQueries.GetDetailsAsync
  → SecureLabDbContext.Incidents → таблиця incidents
  → IncidentDetailsResponse → JSON
  → renderIncidentDetails → textContent / createTextNode
```

Маршрутне обмеження `:guid` відсікає значення, що не є UUID, до виклику коду. Синтаксично коректний, але відсутній UUID дає `null` від query, а endpoint перетворює його на 404 Problem Details.

## Межі довіри

| Межа або перехід | Дані, що її перетинають | Що не можна припускати | Контроль у маршруті |
|---|---|---|---|
| браузер → API | method, URL, `status`, `{id}` | що запит створено з UI (`<select>`, кнопка) | allowlist `status`, обмеження `:guid`, 400 для некоректного значення, 404 для відсутнього ресурсу |
| API → PostgreSQL | умови запиту (`id`, `status`) | що збережений текст безпечний | параметризований запит EF Core, `AsNoTracking()`, явна проєкція |
| API → браузер | JSON response | що право читати entity означає право бачити всі поля | окремі response DTO без `OwnerUserId`, email і внутрішніх коментарів |
| response → DOM | текстові поля з JSON | що текст можна інтерпретувати як HTML | `textContent`, `document.createTextNode`, без `innerHTML` |
| конфігурація → API | connection string, змінні середовища | що локальна конфігурація придатна для іншого середовища | `ConnectionStrings__SecureLab` задається поза Git; реальні значення не комітяться |

Frontend не є серверним контролем: будь-який HTTP-клієнт може надіслати запит без форми й кнопки.

## Конфігураційні входи та залежності

| Вхід | Що визначає | Хто читає |
|---|---|---|
| `global.json` | версія .NET SDK (смуга 10.0.3xx, `rollForward: latestPatch`) | `dotnet` CLI |
| `infra/.env.example` | `POSTGRES_PORT` | `docker compose` |
| `infra/compose.yaml` | контейнер PostgreSQL, локальні навчальні облікові дані, порт 54329 | `docker compose` |
| `appsettings.json` | рівні логування, `IncludeScopes`, `Database:AllowReset=false` | API |
| `appsettings.Development.json` | локальний connection string, дозвіл на reset і міграції у Development | API (Development) |
| `ConnectionStrings__SecureLab` (змінна середовища) | перевизначає connection string | API |
| `Properties/launchSettings.json` | URL `http://localhost:5080`, `ASPNETCORE_ENVIRONMENT=Development` | `dotnet run` |
| `SecureLabApiFactory` | тести запускають API в Development, тобто на тій самій локальній БД | інтеграційні тести |

Пріоритет конфігурації: `appsettings.json` < `appsettings.Development.json` < змінні середовища. Зміна порту PostgreSQL потребує узгодження `POSTGRES_PORT` і `ConnectionStrings__SecureLab`.

## Журналювання

У `Program.cs` кожен запит виконується в scope з `TraceId` (`Activity.Current?.Id`), який збігається з `traceId` у Problem Details, тому помилку з відповіді можна знайти в журналі. `IncidentQueries.GetSeveritySummaryAsync` журналює кількість груп і фільтр `status`. Описи інцидентів, connection string, cookies і токени не журналюються.

## Повернення до відомого seed-стану

Спочатку зупинити API (`Ctrl+C`), потім:

```text
dotnet run --no-build --configuration Release --project src/SecureLab.Api -- --reset-database
```

Команда очищає лише навчальні таблиці й відновлює seed; працює тільки в Development.

## Знахідки в starter

1. `GET /api/incidents?status=1` і `?status=New,Triaged` повертали 200, бо `Enum.TryParse` + `Enum.IsDefined` приймають числа й списки. Виправлено явним allowlist назв у `IncidentEndpoints.TryParseStatus`; тепер 400.
2. `GET /api/does-not-exist` повертав 200 з `text/html` через запасний маршрут на `index.html`. Виправлено маршрутом `/api/{**path}` у `Program.cs`; тепер 404 `application/problem+json`.
3. Пропозиція: `scripts/test.ps1` перед тестами відновлює seed, бо тести використовують ту саму БД, що й стенд, і без reset результат залежить від попередніх експериментів.