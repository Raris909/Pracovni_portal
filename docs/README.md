# Pracovní portál

- Web: Blazor WebAssembly; backend: ASP.NET Core; obojí .NET 10.
- Databáze: PostgreSQL 17; HTTPS a reverse proxy: Caddy.
- Všechny příkazy v dokumentaci spouštět z kořene repozitáře.

## Obsah

| Dokument | Obsah |
| --- | --- |
| [Vývoj](development.md) | Lokální spuštění, ladění, testy |
| [Architektura a API](architecture.md) | Komponenty, endpointy, autentizace |
| [Databáze](database.md) | Tabulky, vazby, inicializace |
| [Nasazení](deployment.md) | Konfigurace, HTTPS, záloha |

## Rychlé spuštění

Požadavky: Docker s Compose v2, Bash, OpenSSL.

```bash
# Pouze pokud .env ještě neexistuje.
bash scripts/setup-env.sh
docker compose up --build -d
```

Otevřít **https://localhost**. Důvěru místnímu certifikátu nastavit podle [nasazení](deployment.md).
