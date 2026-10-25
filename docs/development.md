# Vývoj

Požadavky: .NET 10 SDK, Docker s Compose v2, Bash, OpenSSL.

## Lokální spuštění

Při prvním použití vytvořit `.env`: `bash scripts/setup-env.sh`.

```bash
docker compose -f compose.yaml -f compose.dev.yaml up -d --wait db
dotnet watch --project src/Portal.Api
```

Ve druhém terminálu:

```bash
dotnet watch --project src/Portal.Web
```

| Služba | Adresa |
| --- | --- |
| Web | http://localhost:5180 |
| API | http://localhost:5080 |
| OpenAPI JSON | http://localhost:5080/openapi/v1.json |
| PostgreSQL | `localhost:5433`, databáze/uživatel `portal` |

- API v `Development` načítá kořenový `.env`; po změně restartovat API.
- Priorita konfigurace: argumenty → proměnné prostředí → `.env` → `appsettings.json`.
- Změna portů: upravit také [ApiBaseUrl](../src/Portal.Web/wwwroot/appsettings.Development.json) a CORS v [Program.cs](../src/Portal.Api/Program.cs).
- Lokální API a API z vývojového Compose sdílejí port `5080`; spouštět jen jedno.

## VS Code

- Nainstalovat doporučená rozšíření z [.vscode/extensions.json](../.vscode/extensions.json).
- **Portal: Full stack** → `F5`: sestavení, databáze, API, web; ladění webu používá Chrome.
- Task **dev: watch**: oba projekty s hot reloadem; web otevřít ručně.
- Task **db: stop**: zastavení databáze se zachováním dat.

## Sestavení a testy

```bash
dotnet restore Portal.sln
dotnet build Portal.sln -c Release --no-restore
dotnet test Portal.sln -c Release --no-build
```

| Projekt | Pokrytí | Potřebuje Docker |
| --- | --- | --- |
| `Portal.Api.Tests` | Podpis JWT, role, expirace | Ne |
| `Portal.IntegrationTests` | Registrace, login, účet, validace, přístupy, readiness | Ano |

- Integrační testy používají vlastní dočasný PostgreSQL; Compose DB ani `.env` nepoužívají.
- Docker daemon musí běžet; databázové testy se při jeho nedostupnosti nepřeskakují.
- Pouze unit testy: `dotnet test tests/Portal.Api.Tests`.
- Změny schématu: [databáze](database.md).
