# Nasazení

Spuštění: [rychlý start](README.md). Konfigurace: [.env.example](../.env.example), [compose.yaml](../compose.yaml).

## Služby

| Služba | Úloha | Veřejné porty | Trvalé volumes |
| --- | --- | --- | --- |
| `web` | Caddy, WASM, reverse proxy | 80, 443 | `caddy_data`, `caddy_config` |
| `api` | REST API, SignalR | Žádné | — |
| `db` | PostgreSQL 17 | Žádné | `postgres_data` |

## `.env`

| Proměnná | Hodnota / účel |
| --- | --- |
| `POSTGRES_PASSWORD` | Heslo DB; generuje setup skript |
| `JWT__SIGNINGKEY` | Podpisový klíč, min. 64 znaků; generuje setup skript |
| `CONNECTIONSTRINGS__PORTAL` | Lokální API: `localhost:5433`; heslo z `${POSTGRES_PASSWORD}` |
| `SITE_ADDRESS` | `localhost` nebo veřejná doména |
| `HTTP_PORT`, `HTTPS_PORT` | Výchozí `80`, `443` |

- `.env` necommitovat; setup existující soubor nepřepisuje.
- Compose nastavuje API připojení na `db:5432`.
- Změna hesla v `.env` nezmění heslo existující DB; změnit obě společně.
- Změna podpisového klíče zneplatní vydané JWT.

## Místní HTTPS

Po prvním spuštění exportovat veřejný kořenový certifikát Caddy:

```bash
mkdir -p artifacts
docker compose cp web:/data/caddy/pki/authorities/local/root.crt artifacts/portal-local-ca.crt
```

- Importovat `artifacts/portal-local-ca.crt` do důvěryhodných autorit OS/prohlížeče.
- Ověření bez importu do OS:

```bash
curl --cacert artifacts/portal-local-ca.crt https://localhost/health/ready
```

- Pro jiné porty nastavit např. `HTTP_PORT=8080`, `HTTPS_PORT=8443`.
- Potom používat přímo `https://localhost:8443`; HTTP přesměrování vlastní mapovaný port nezohlední.

## Veřejná doména

1. Nastavit `SITE_ADDRESS=jobs.example.com`; DNS nasměrovat na server.
2. Zpřístupnit porty 80/443; Caddy zajistí certifikát a obnovování.
3. Spustit `docker compose up --build -d`; zachovat Caddy volumes.

Používat pouze základní Compose. Vývojový override zveřejňuje lokální DB/API porty a zapíná `Development`.

## Provoz a záloha

```bash
docker compose ps
docker compose logs --tail=100 api web db
```

Záloha před změnou schématu; kopii uchovávat mimo server:

```bash
mkdir -p artifacts
docker compose exec -T db pg_dump -U portal -d portal -Fc > artifacts/portal.dump
```

- `docker compose down`: zastaví a odstraní kontejnery; data zachová.
- `docker compose down -v`: odstraní také volumes včetně databáze a certifikátů.
