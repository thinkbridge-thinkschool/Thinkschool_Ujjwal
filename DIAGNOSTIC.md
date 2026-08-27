# Diagnostic Report — thinkschool

Generated: 2026-08-24. Diagnostic only — no fixes applied.

## 1. Git state

**Current branch:** `main`

**`git branch -a`:**
```
  ai-assisted-work
  anemic-to-rich
  day10-tracking
  day11-profile
  day12-cqrs
  day3-authorization-policies
  day3-integration-tests
  day3-lockdown
  day3-testcontainers
  day3-unit-tests
  day4-appinsights
  day4-ci
  day4-coverage
  day4-options
  day4-otel
  day4-serilog
  day5-azd
  day5-container
  day5-diagnose
  day5-polly
  day7-sql
  day8-indexes
  day9-isolation
* main
  remotes/origin/HEAD -> origin/main
  remotes/origin/ai-assisted-work
  remotes/origin/anemic-to-rich
  remotes/origin/day10-tracking
  remotes/origin/day11-profile
  remotes/origin/day12-cqrs
  remotes/origin/day3-authorization-policies
  remotes/origin/day3-integration-tests
  remotes/origin/day3-lockdown
  remotes/origin/day3-testcontainers
  remotes/origin/day3-unit-tests
  remotes/origin/day4-appinsights
  remotes/origin/day4-ci
  remotes/origin/day4-coverage
  remotes/origin/day4-options
  remotes/origin/day4-otel
  remotes/origin/day4-serilog
  remotes/origin/day5-azd
  remotes/origin/day5-container
  remotes/origin/day5-diagnose
  remotes/origin/day5-polly
  remotes/origin/day7-sql
  remotes/origin/day8-indexes
  remotes/origin/day9-isolation
  remotes/origin/main
```

**Commits ahead of `main` per local `dayN` branch** (`git rev-list --count main..<branch>`):

| Branch | Commits ahead of main |
|---|---|
| day10-tracking | 0 |
| day11-profile | 2 |
| day12-cqrs | 2 |
| day3-authorization-policies | 0 |
| day3-integration-tests | 0 |
| day3-lockdown | 0 |
| day3-testcontainers | 1 |
| day3-unit-tests | 0 |
| day4-appinsights | 9 |
| day4-ci | 3 |
| day4-coverage | 4 |
| day4-options | 8 |
| day4-otel | 7 |
| day4-serilog | 6 |
| day5-azd | 17 |
| day5-container | 15 |
| day5-diagnose | 13 |
| day5-polly | 19 |
| day7-sql | 4 |
| day8-indexes | 0 |
| day9-isolation | 2 |

Several branches (day10-tracking, day3-authorization-policies, day3-integration-tests, day3-lockdown, day3-unit-tests, day8-indexes) show **0** commits ahead — their tip is already merged into (or is an ancestor of) `main`.

**`git status --short`:**
```
?? day-11/
?? day-5/QuotesApi/quotes.db
?? day-5/QuotesApi/quotes.db-shm
?? day-5/QuotesApi/quotes.db-wal
?? day-7/
```
Note: `day-5/QuotesApi/quotes.db` is also listed in `.gitignore` (`QuotesApi/quotes.db`), but shows as untracked (`??`) rather than ignored — this is expected since the `-shm`/`-wal` sidecar files aren't covered by that ignore line, and git still reports the parent pattern match inconsistently for `--short` without `-uall`; regardless, none of these four paths are tracked.

**day-N folders present in the working tree right now:**
```
day-1  day-2  day-3  day-4  day-5  day-7  day-8  day-9  day-10  day-11  day-12
```
(`day-6` does not exist in the working tree.)

**Which branch each day-N folder was last committed on** (via `git log --all -1 -- day-N/` then `git branch --all --contains <that commit>`):

| Folder | Last commit (date) | Branches containing that commit |
|---|---|---|
| day-1 | 98ac0c0 (2026-08-13) | day4-appinsights, day4-ci, day4-coverage, day4-options, day4-otel, day4-serilog, day5-azd, day5-container, day5-diagnose, day5-polly (+ their remotes) |
| day-2 | — no commit touches this path — | n/a (see below) |
| day-3 | 980aa21 (2026-08-12) | day3-testcontainers (+ remote) |
| day-4 | ee58ea0 (2026-08-13) | day4-appinsights, day5-azd, day5-container, day5-diagnose, day5-polly (+ remotes) |
| day-5 | ccd05bb (2026-08-20) | day11-profile, day12-cqrs, **main** (+ remotes) |
| day-7 | 0fbe701 (2026-08-17) | day7-sql (+ remote) |
| day-8 | 7fb45d6 (2026-08-18) | day11-profile, day12-cqrs, day8-indexes, **main** (+ remotes) |
| day-9 | 94e455f (2026-08-19) | day9-isolation (+ remote) |
| day-10 | 6097853 (2026-08-20) | day11-profile, day12-cqrs, **main** (+ remotes) |
| day-11 | 36bb933 (2026-08-21) | day11-profile (+ remote) |
| day-12 | 939f89d (2026-08-22) | day12-cqrs (+ remote) |

**day-2 anomaly:** the `day-2` directory exists on disk but is completely empty (`ls -la` shows only `.`/`..`, 0 entries) and `git ls-files day-2` returns nothing. No commit on any branch ever touched `day-2/`. It is an empty, untracked directory with no git history — not associated with any branch.

**day-11 and day-7 in working tree vs. git status:** `day-11/` and `day-7/` show as untracked (`??`) in `git status --short` on the current branch (`main`) even though each has committed history — because that history lives only on branches `day11-profile` and `day7-sql` respectively, which have not been merged into `main`. The directories exist in the working tree (left over from checking out/working on those branches, or copied in) but `main`'s tree doesn't track them, hence untracked.

---

## 2. day-5 QuotesApi — why it fails to run

**Working directory confirmed:** `/Users/ujjwalsrivastava/thinkschool`, and `day-5/QuotesApi/` exists with a full ASP.NET Core project (`Program.cs`, `QuotesApi.csproj`, `Extensions/`, `Data/`, `Migrations/`, etc.).

### Connection string resolution

No `ConnectionStrings` section exists anywhere in `appsettings.json` or `appsettings.Development.json` (confirmed via `grep -rn "ConnectionStrings\|Data Source" appsettings*.json` — zero matches).

`Extensions/InfrastructureExtensions.cs`:
```csharp
services.AddDbContext<QuotesDbContext>(options =>
    options.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=quotes.db"));
```
Since `ConnectionStrings:Default` is never configured, `config.GetConnectionString("Default")` returns `null`, and EF Core falls back to the hardcoded literal `"Data Source=quotes.db"`. This is a **relative path**, so it resolves relative to the process's current working directory at runtime — i.e., wherever `dotnet run` (or the built binary) is launched from. When run from `day-5/QuotesApi/`, that's `day-5/QuotesApi/quotes.db`.

### Program.cs migration call

`Program.cs` lines 38–49:
```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    db.Database.Migrate();

    // Seeded credentials must never be created in a deployed environment.
    if (app.Environment.IsDevelopment() && !db.Users.Any())
    {
        db.Users.Add(User.Create("test@example.com", "Password123!"));
        db.SaveChanges();
    }
}
```
Confirmed: `db.Database.Migrate()` **is** called at startup, before `app.MapQuoteEndpoints()` (line 51) and `app.Run()` (line 64).

### *.db files under day-5

```
-rw-r--r--  1 ujjwalsrivastava  staff  16512  day-5/QuotesApi/quotes.db-wal
-rw-r--r--  1 ujjwalsrivastava  staff  32768  day-5/QuotesApi/quotes.db-shm
-rw-r--r--  1 ujjwalsrivastava  staff  45056  day-5/QuotesApi/quotes.db
```
Only one `.db` file exists under `day-5/` (plus its WAL/SHM journal sidecar files), located at `day-5/QuotesApi/quotes.db` — the same path the fallback connection string resolves to when run from that directory.

### Contents of day-5/QuotesApi/quotes.db

```
$ sqlite3 quotes.db ".tables"
CollectionItems        RefreshTokens          __EFMigrationsLock
Collections            Users
Quotes                 __EFMigrationsHistory

$ sqlite3 quotes.db "SELECT COUNT(*) FROM Quotes;"
3
```
The `Quotes` table exists and has 3 rows. Schema:
```sql
CREATE TABLE IF NOT EXISTS "Quotes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Quotes" PRIMARY KEY AUTOINCREMENT,
    "Author" TEXT NOT NULL,
    "Text" TEXT NOT NULL
, "CreatedByUserId" TEXT NULL);
```
**This database file, on its own, is fine** — migrations are applied and it has data.

### Actually running `dotnet run`

Ran `dotnet run` from `day-5/QuotesApi/`, waited for it to bind port 5296. Full captured output:

```
Using launch settings from /Users/ujjwalsrivastava/thinkschool/day-5/QuotesApi/Properties/launchSettings.json...
Building...
.../QuotesApi.csproj : warning NU1903: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known high severity vulnerability, https://github.com/advisories/GHSA-2m69-gcr7-jv3q
11:12:21 [WRN] The entity type 'CollectionItem' has composite key '{'CollectionId', 'QuoteId'}' which is configured to use generated values. SQLite does not support generated values on composite keys.
11:12:21 [INF] Acquiring an exclusive lock for migration application...
... (EF Core migration-check SQL, all successful) ...
11:12:21 [INF] No migrations were applied. The database is already up to date.
... (seed-check SQL against Users table, successful) ...
11:12:22 [ERR] Hosting failed to start
System.IO.IOException: Failed to bind to address http://127.0.0.1:5296: address already in use.
 ---> Microsoft.AspNetCore.Connections.AddressInUseException: Address already in use
 ---> System.Net.Sockets.SocketException (48): Address already in use
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(...)
   ...
   at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketConnectionListener.Bind()
   ...
Unhandled exception. System.IO.IOException: Failed to bind to address http://127.0.0.1:5296: address already in use.
   ...
   at Program.<Main>$(String[] args) in /Users/ujjwalsrivastava/thinkschool/day-5/QuotesApi/Program.cs:line 64
```

So: **the app builds, EF Core migrations run cleanly against day-5's own `quotes.db`, the seed check succeeds — and then Kestrel crashes because port 5296 is already occupied.** The day-5 app itself never actually gets a chance to serve traffic; it dies on the bind step.

### What's actually holding port 5296

```
$ lsof -i :5296
COMMAND     PID  USER              FD   TYPE  ... NAME
QuotesApi 25401  ujjwalsrivastava  363u IPv4   ... localhost:5296 (LISTEN)
QuotesApi 25401  ujjwalsrivastava  364u IPv6   ... localhost:5296 (LISTEN)

$ ps -o pid,lstart,etime,command -p 25401
  PID STARTED                          ELAPSED COMMAND
25401 Fri Aug 21 16:12:33 2026     02-19:00:09 /Users/ujjwalsrivastava/thinkschool/day-11/QuotesApi/bin/Debug/net10.0/QuotesApi --urls http://localhost:5296
```
A **stale process from `day-11/QuotesApi`** (a different, older build, not day-5) has been running continuously since Friday 2026-08-21 16:12 — about 2 days 19 hours before this diagnostic — and is squatting on port 5296. There's also a parent `dotnet run --urls http://localhost:5296` (PID 25383) from that same stale launch still alive.

### curl result

```
$ curl -s -w "\nHTTP_STATUS:%{http_code}\n" http://localhost:5296/api/quotes
{"title":"An unexpected error occurred","status":500,"detail":"SQLite Error 1: 'no such table: Quotes'."}
HTTP_STATUS:500
```
This response came from the **stale day-11 process**, not from day-5 (day-5 never bound the port). The day-11 process fails with `no such table: Quotes` — its own connection string/working directory points at a `quotes.db` that lacks the `Quotes` table (day-11's project layout/db differs from day-5's; not investigated further per scope of this diagnostic).

### Bottom line

The day-5 API's code, config, and database are **not** the problem — build succeeds, migrations apply cleanly, the DB has the `Quotes` table with 3 rows, and `Program.cs` correctly calls `db.Database.Migrate()`. The actual failure is purely **environmental**: an old, unrelated `day-11/QuotesApi` process (running since 2026-08-21, PID 25401/25383) never got shut down and is holding port 5296, so day-5's own `dotnet run` throws `AddressInUseException` and crashes before Kestrel can start. Any `curl` against port 5296 right now hits that stale day-11 process instead, which itself 500s with `no such table: Quotes` because its own database context doesn't match day-5's.

(No fix was applied — the stale processes were left running, per the "diagnostic only" instruction. The `dotnet run` instance started for this diagnostic crashed on its own after the bind failure and left no additional process behind.)

---

## 3. Node / Angular readiness

```
$ node --version
v24.19.0

$ npm --version
11.17.0

$ npx --version
11.17.0

$ npm ls -g @angular/cli
/Users/ujjwalsrivastava/.nvm/versions/node/v24.19.0/lib
`-- @angular/cli@22.1.3

$ npx --no-install ng version
     _                      _                 ____ _     ___
    / \   _ __   __ _ _   _| | __ _ _ __     / ___| |   |_ _|
   / △ \ | '_ \ / _` | | | | |/ _` | '__|   | |   | |    | |
  / ___ \| | | | (_| | |_| | | (_| | |      | |___| |___ | |
 /_/   \_\_| |_|\__, |\__,_|_|\__,_|_|       \____|_____|___|
                |___/

Angular CLI       : 22.1.3
Node.js           : 24.19.0
Package Manager   : npm 11.17.0
Operating System  : darwin arm64
```
Node 24.19.0, npm 11.17.0, and npx 11.17.0 are installed. `@angular/cli` 22.1.3 is installed globally and `ng version` runs successfully. **Node/Angular tooling is ready for the next task.**
