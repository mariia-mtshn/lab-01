# Звіт WEB ЛР 2

## 1. Ідентифікація

- ПІБ і група: Матіїшин Марія Тарасівна, КБ-43.
- Спільний baseline 2-A «Трекер інцидентів»; індивідуального номера немає.
- Заявлений рівень: відмінний («Відмінно»).
- Гілка `lab/2-input-sqli`: `lab/2-sqli`.
- Release, base_commit та entry_parent із квитанції `.scaffolds/lab-02.json`:
  - release: `lab-02-start-v1`
  - base_commit: `c44d414bdd65c81b1d60e68aebb5d90701276c0f`
  - entry_parent: `ee72ed9b8e3913ad02de7230b140211d46f85b3e`
- Vulnerable commit: `9b2972a57416e86585dc5b6189aa1116f4ab9d92`
- Fixed commit коду: `f697f76fb5c4475f259db48b20adc77056c2f385`
- Фінальний тег `v0.2.0` стоїть на commit цього звіту (створений після fixed commit; код між ними не змінювався).


## 2. Контракт і CP-01

Зовнішній контракт `POST /api/incidents` описує лише поля, які може надіслати клієнт.

| Категорія | Поля | Рішення сервера |
|---|---|---|
| клієнт надсилає | `title`, `description`, `severity`, `occurredAtUtc` | приймає тільки через `CreateIncidentRequest` і перевіряє |
| сервер визначає | `id`, `ownerUserId`, `status`, `createdAtUtc`, `updatedAtUtc` | у DTO немає; `Id = Guid.NewGuid()`, власник — навчальний користувач Alice, `Status = New`, часи = серверний UTC |
| API повертає | `id`, `title`, `severity`, `status`, `occurredAtUtc`, `createdAtUtc` | окремий `CreatedIncidentResponse`, entity напряму не серіалізується |

Серверні перевірки (усі виконуються в одному обробнику **до** створення entity та доступу до БД; помилки збираються в `Dictionary<string, string[]>` і повертаються через `Results.ValidationProblem(errors)`):

- `title`: не порожній після `Trim()`; довжина вхідного значення до `Trim()` не більша за 160. Ключ помилки `title`.
- `description`: не порожній після `Trim()`; довжина не більша за 4000. Ключ `description`.
- `severity`: `Enum.TryParse<IncidentSeverity>(..., ignoreCase: true)` і `Enum.IsDefined`. Ключ `severity`.
- `occurredAtUtc`: обов'язкове (`DateTimeOffset?`), не пізніше ніж `now + 5 хв`, де `now` — серверний UTC, зафіксований один раз. Перед записом викликається `.ToUniversalTime()`, тому `13:00+03:00` і `10:00Z` — той самий момент.
- cross-field (T-09): для `High` та `Critical` `description` після `Trim()` має містити щонайменше 40 символів. Ключ `description`.
- власне правило T-10: `description` після `Trim()` не може дорівнювати `title`. Ключ `description`, код 400.
- предметний конфлікт (T-03): якщо вже є інцидент із тим самим (після `Trim()`, з урахуванням регістру) `title` у статусі New, Triaged, InProgress або Resolved, повертається 409. Статус Closed не блокує.

**CP-01.** `Enum.TryParse` недостатній, бо приймає числовий рядок: `"7"` успішно перетворюється на `(IncidentSeverity)7`, хоча такого елемента в enum немає. `Enum.IsDefined` перевіряє належність до оголошеного набору, тому разом вони відхиляють `severity: "7"` з 400 (див. T-02). Маршрут такий: JSON → `CreateIncidentRequest` → серверна перевірка → `ValidationProblem` → 400 `application/problem+json`. `<select>` у браузері лише підказка для користувача: будь-який HTTP-клієнт (curl, `.http`-файл) надішле довільне значення, тож перевірка мусить бути на сервері.

Захист від overposting: `CreateIncidentRequest` не має властивостей `Id`, `OwnerUserId`, `Status`, `CreatedAtUtc`, тому зайві поля JSON ігноруються. Запит із `id`, `ownerUserId`, `status: "Closed"`, `createdAtUtc` створив запис з новим `id`, статусом `New` і власником Alice («Аліса Коваль») (див. A-02, `evidence/a02-response.txt`).

## 3. Security-сценарій і CP-02/CP-03

1. **Контекст і гіпотеза.** Локальний endpoint `GET /api/incidents/search` отримує недовірені `q` і `sortBy`. Існує ризик, що значення увійде до SQL-структури. Робота виконувалась лише локально (Development, PostgreSQL з `infra/compose.yaml`, штучний seed).
2. **Стан до.** Vulnerable commit `9b2972a57416e86585dc5b6189aa1116f4ab9d92`. Метод — обробник `GET /api/incidents/search` у `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs`. Точне місце: рядок `var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "") + …`, де `q` конкатенується з SQL-текстом. `sortBy` потрапляє в `ORDER BY` через гілку `_ => sortBy`. Виклик: `db.Incidents.FromSqlRaw(sql)`. (**Вказати фактичні номери рядків зі свого vulnerable commit.**)
3. **Мінімальний PoC.** Власний read-only сценарій C (`evidence/s01-c-response.txt`): контрольний ввід `zz-no-match' OR TRUE -- ` без руйнівних дій і лише читання.
4. **Спостереження.**

   | Запит | Результат на vulnerable commit |
   |---|---|
   | A: `q=USB` | 200, 1 запис |
   | B: `q=zz-no-match` | 200, `[]`, `Parameters=[]` |
   | C: контрольний ввід | 200, **усі 5** записів seed (вибірку небажано розширено) |
   | D: `q=O%27Brien` | 500, Problem Details без внутрішніх деталей |
   | `q=комп'ютерного` | 500 |

   Запити B і C відрізняються тим, що в C апостроф закрив рядковий літерал SQL, `OR TRUE` додав умову, яка завжди істинна, а `--` закоментував решту рядка. У D апостроф легального значення так само закрив літерал, а решта тексту (`Brien%' …`) стала некоректним SQL (SQLSTATE 42601), тому клієнт отримав 500. У журналі API для B і C видно `Parameters=[]`: значення передано всередині тексту команди.
5. **Першопричина.** Значення `q` (і `sortBy`) з'єднувались із SQL-текстом до виконання, тому СУБД не могла відрізнити дані від структури. Фільтр апострофа чи слова `OR` не усуває причину: він реагує на конкретний payload, а не розділяє значення і структуру, до того ж ламає легітимні дані (O'Brien).
6. **Виправлення (CP-03).** Пошук переписано на LINQ з `EF.Functions.ILike(…, pattern, "\\")`. Значення `q` екранується функцією `EscapeLike` (`\`, потім `%` та `_`), обрамлюється `%…%` і передається EF Core **параметром**. Структуру сортування вибирає серверний `switch` (allowlist `createdAtUtc`, `severity`, `status`) з рангами за контрактом; для рівних значень `ThenBy(Id)`; після сортування `Take(50)`. Невідомий `sortBy` повертає 400 із ключем `sortBy` і до SQL не доходить. Diff: `git diff 9b2972a57416e86585dc5b6189aa1116f4ab9d92..f697f76fb5c4475f259db48b20adc77056c2f385` (`evidence/security-diff.txt`). Тепер у журналі `Parameters=[@pattern='?', …]` і `ILIKE @pattern ESCAPE '\'`: значення передається окремо від тексту SQL.
7. **Retest і позитивна регресія.** Після reset на fixed commit: A повертає той самий запис; B та C повертають `[]`; D — 200 і «O'Brien classroom report»; `комп'ютерного` — 200 і «Перевірка журналу комп'ютерного класу»; `q=%25` (буквальний `%`) — `[]`; `sortBy=price` — 400.
8. **Залишковий ризик.** Перевірка не замінює authorization (власник призначається сталим навчальним контекстом до ЛР 3), rate limit, пагінацію понад `Take(50)`, перевірку всіх інших endpoint та production logging policy.

## 4. Фактичні перевірки DEL-02

| ID | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|
| T-01 | Коректне створення | reset, унікальний title, valid DTO | `POST /api/incidents` | 201, response DTO без зайвих полів | 201, `Location: /api/incidents/{id}`, у тілі лише `id`, `title`, `severity`, `status`, `occurredAtUtc`, `createdAtUtc` | `evidence/t01-response.txt` |
| T-02 | Некоректний DTO | `severity: "7"`; також порожній title, немає/майбутня дата | `POST /api/incidents` | 400 `application/problem+json` | 400, `application/problem+json`, `errors` з ключем `severity` (та відповідними ключами для інших випадків), без stack trace/SQL | `evidence/t02-response.txt`; автотест `T02_InvalidSeverity7_Returns400ProblemJson` |
| T-03 | Предметний конфлікт | існує інцидент New з тим самим title | повторний `POST` з тим самим title | 409 у Problem Details | 409, `application/problem+json`, `title`, `status`, `detail`, без внутрішніх деталей | `evidence/t03-response.txt`; автотест `T03_DuplicateActiveTitle_Returns409` |
| S-01 | SQLi до виправлення | vulnerable commit, local seed | read-only сценарій C | небажано розширена вибірка | 200, повернуто всі 5 записів seed замість `[]` | `evidence/s01-c-response.txt`; commit `9b2972a5…` |
| S-02 | Retest SQLi | fixed commit, той самий seed | той самий контрольний ввід C (автотест) | `[]`, додатково `q=USB` повертає рівно один запис | 200, `[]`; позитивний контроль: рівно один запис `…0005` | `evidence/s02-response.txt`; автотест `S02_ControlInput_ReturnsEmptySet_AndNormalSearchStillWorks` |
| T-04 | Позитивна регресія | fixed commit | `q=USB`, `q=O%27Brien`, `q=комп'ютерного` | коректні записи, без 500 | 200 для всіх; знайдено відповідні записи, 500 немає | `evidence/t04-response.txt` |
| T-05 | Невідоме сортування | fixed commit, reset | `GET /api/incidents/search?sortBy=price` | 400 | 400, `application/problem+json`, `errors.sortBy`, без SQL/stack trace | `evidence/t05-response.txt`; автотест `T05_UnknownSortBy_Returns400WithSortByKey` |
| T-06 | Ресурс не знайдено | baseline endpoint, відсутній id | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 у Problem Details | 404, `application/problem+json`, title «Інцидент не знайдено» | `evidence/t06-response.txt` |
| T-09 | Cross-field | High, унікальні title, решта коректна | два `POST` з description 39 та 40 символів | перший 400 (`description`), другий проходить | 39 символів: 400, ключ `description`; 40 символів: 201 | `evidence/t09-39.txt`, `evidence/t09-40.txt`; автотест `T09_HighSeverity_Description39Fails_40Passes` |
| T-10 | Додаткова предметна перевірка | правило й контрольний допустимий стан описано нижче | порушити лише це правило | 400, контрольний допустимий сценарій успішний | недопустимий запит: 400, ключ `description`; допустимий: 201 | `evidence/t10-invalid.txt`, `evidence/t10-valid.txt` |
| A-01 | Огляд data-access points | fixed commit | `data-access-scan.py`, `git grep`, ручний огляд LINQ | для кожного збігу файл/метод, категорія, висновок | таблиця огляду нижче; небезпечних місць не залишилось | таблиця A-01 у цьому звіті; `evidence/a01-scan.txt` |
| A-02 | Mass-assignment | fixed commit, локалізований ризик | POST із зайвими `id`, `ownerUserId`, `status`, `createdAtUtc` (автотест) | зайві поля ігноруються | створено запис із новим id, статусом `New`, власником Alice («Аліса Коваль») | `evidence/a02-response.txt`; автотест `A02_Overposting_ServerManagedFieldsAreIgnored` |

**Build і baseline-тести після останньої зміни:** `dotnet build tests/SecureLab.Api.Tests -c Release` — успішно. `dotnet test tests/SecureLab.Api.Tests -c Release` — **Passed: N, Failed: 0** (`evidence/test-output.txt`). *(22/22)*

### T-10: додаткова предметна перевірка

- Правило: `description` після `Trim()` не може дорівнювати `title` (порівняння ординальне).
- Межа: збіг (рівність) значень; решта значень допустима.
- Ключ помилки: `description`. Код відповіді: **400**.
- Допустимий запит: `title: "Same text ok"`, `description: "Different text"`, `severity: "Low"`, `occurredAtUtc: "2026-09-01T10:00:00Z"` — 201.
- Недопустимий запит: `title: "Same text"`, `description: "Same text"`, ті самі `severity` і `occurredAtUtc` — 400, `errors.description`.
- Перевірка виконується в тому самому обробнику до доступу до БД, не послаблює і не змінює правил 2-A.

### A-01: огляд data-access points (відмінний, повний)

| Файл:рядок / метод | Категорія | Висновок |
|---|---|---|
| `Data/DatabaseBootstrap.cs:29`, `ExecuteSqlRawAsync` (reset) | trusted static SQL, змінних значень немає | безпечно: дані клієнта в текст не потрапляють; запускається лише через `--reset-database` |
| `Scaffolding/Lab02Endpoints.cs`, `GET /api/incidents/search` | LINQ, `EF.Functions.ILike`, значення параметром `@pattern` | безпечно: `q` екрановано й передано параметром; сортування за allowlist |
| `Scaffolding/Lab02Endpoints.cs`, `POST /api/incidents`, `AnyAsync` | LINQ, `title` і статуси параметрами | безпечно |
| `Scaffolding/Lab02Endpoints.cs`, `POST /api/incidents`, `Add` + `SaveChangesAsync` | EF Core, запис entity | безпечно; значення параметризуються EF |
| *(ендпоінти ЛР 1: список, фільтр за status, деталі за id, підсумок за severity — `IncidentEndpoints.cs` / `IncidentQueries.cs`)* | LINQ | *(підставте файл:рядок і підтвердіть, що значення йдуть параметрами)* |
| `Data/DbSeeder.cs`, `Scaffolding/Lab02Seed.cs` | EF Core, статичні штучні дані | безпечно: ввід користувача відсутній |

`git grep -n -F -e '$"SELECT' -e '+ query' -- src tests` на fixed commit нічого не виводить. На vulnerable commit у `Lab02Endpoints.cs` додатково було два збіги `FromSqlRaw`/`ORDER BY`.

### A-02: mass-assignment

- До: якби request DTO повторював entity, клієнт міг би підмінити `id`, `ownerUserId`, `status`, `createdAtUtc`. Відтвореного дефекту в коді не було: `CreateIncidentRequest` (`Scaffolding/Lab02Endpoints.cs`, наприкінці файла) таких властивостей не має, тобто ризик усунуто контрактом DTO.
- Після: автотест `A02_Overposting_ServerManagedFieldsAreIgnored` надсилає ці зайві поля й перевіряє новий `id` (не надісланий), статус `New` та власника «Аліса Коваль».
- Перевірка чутливості (локально, не закомічено): тимчасово додано `string? Status` до `CreateIncidentRequest` і `Status = Enum.Parse<IncidentStatus>(request.Status ?? "New")` в обробнику. Тест впав з `Expected: "New", Actual: "Closed"`. Після `git restore` тест знову проходить.

## 5. Обмеження і висновок

Перевірено: серверну валідацію створення інциденту (формат, діапазон, cross-field, предметний конфлікт, власне правило), єдиний формат помилок `application/problem+json` без внутрішніх деталей (400, 404, 409), відсутність mass-assignment, усунення SQL injection у пошуку (параметризація значень `q` та allowlist для `sortBy`), позитивну регресію (звичайний пошук, апостроф, буквальний `%`), автоматичні тести T-02, T-03, T-09, A-02, S-02, T-05.

Обмеження:

- Перевірка `AnyAsync` перед записом навчає предметного конфлікту, але не гарантує інваріант за паралельних створень: два одночасні запити можуть обидва пройти перевірку. У реальному застосунку потрібен унікальний індекс або транзакційне рішення (у ЛР 2 migration не додавались).
- Зіпсований JSON (зайва кома, число замість рядка) у стартовому конвеєрі дає 500 через `BadHttpRequestException` до виклику обробника. Це окремий дефект, він не є результатом валідації й тут не виправлявся.
- Пошук не має authorization (власник призначається сталим навчальним контекстом до ЛР 3), rate limit та пагінації понад `Take(50)`. Огляд охоплює лише поточний код проєкту.

Висновок: першопричину SQL injection усунено розділенням структури SQL і значень (параметризація + allowlist), а не фільтрацією символів. Контракт створення інциденту перевіряється на сервері.

## 7. Передавання

- Bundle `securelab-lr02.bundle` створено командою `git bundle create`, перевірено `git bundle verify` («is okay») та `git bundle list-heads` (є `lab/2-input-sqli`, `v0.2.0`).
- У Moodle завантажено звіт, папку `docs/evidence/` та bundle (або один ZIP).
- Git diff та історію переглянуто (`git diff --staged`, `git log --all --name-only`): секретів, `.env` з реальними значеннями, дампів БД і журналів з credentials немає.