# Pracovni_portal

## Rychle spusteni

```bash
bash scripts/setup-env.sh
docker compose up --build -d
```

## zastaveni

```bash
docker compose down
```

## Chyba hesla databaze po zmene .env

Pokud API hlasi `password authentication failed for user "portal"`, ulozena
databaze muze mit jine heslo nez aktualni `.env`. PostgreSQL pouzije
`POSTGRES_PASSWORD` pouze pri prvnim vytvoreni databaze; restart kontejneru heslo
ve stavajicim svazku nezmeni.

Pro lokalni databazi srovnej heslo s `.env` bez smazani dat:

```bash
docker compose -f compose.yaml -f compose.dev.yaml up -d --wait db
docker compose exec -T db psql -v ON_ERROR_STOP=1 -U portal -d portal <<'SQL'
\getenv portal_password POSTGRES_PASSWORD
ALTER ROLE portal WITH PASSWORD :'portal_password';
SQL
```
