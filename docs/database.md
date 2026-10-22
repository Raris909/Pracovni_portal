# Databáze

- PostgreSQL 17; schéma `portal`; 20 tabulek; rozšíření `btree_gist`.
- Definice: [schema.sql](../Database/schema.sql); počáteční data: [seed.sql](../Database/seed.sql).
- Klíče: UUID nebo složené; časové okamžiky: `timestamptz`; snímky a AI výsledky: JSONB.
- Schéma zahrnuje i funkce, které API zatím neposkytuje.

## Tabulky

| Oblast | Tabulky |
| --- | --- |
| Účty | `roles`, `users` |
| Profil | `candidate_profiles`, `candidate_experiences`, `candidate_education` |
| Firmy | `companies`, `company_members` |
| Dokumenty | `candidate_documents`, `application_documents` |
| Nabídky | `job_offers`, `recruitment_stages` |
| Nábor | `applications`, `application_stages`, `candidate_evaluations` |
| Pohovory | `interview_slots`, `interview_bookings` |
| AI | `candidate_cv_reviews`, `application_cv_reviews` |
| Chat | `conversations`, `chat_messages` |


## Důležitá pravidla

- E-mail je unikátní bez rozlišení velikosti písmen; uchazeč má nejvýše jednu přihlášku na nabídku.
- Složené FK hlídají shodu firmy, nabídky a vlastníka dokumentu; autorizaci zajišťuje API, RLS není zavedené.
- Dokument nelze přepsat; nové CV znamená nový záznam. Backend musí zachovat snímek profilu a přílohy odeslané přihlášky.
- Nejvýše jedna nezrušená rezervace na termín a na přihlášku/kolo; aktivní termíny stejného člena firmy se nepřekrývají.
- Historické vazby převážně brání smazání; nabídky archivovat, účty deaktivovat.

## Inicializace a změny

1. Prázdný volume: Compose spustí `schema.sql`, potom `seed.sql`.
2. Seed vytvoří pouze role `candidate` a `company`.
3. Registrace uchazeče vytvoří účet a prázdný profil v jedné transakci; firemní registrace pouze účet.

- Restart existující DB SQL skripty znovu nespustí.
- `schema.sql` je jednorázová instalace, nikoli migrace.
- Změny existující DB: záloha → samostatný SQL skript → ruční aplikace; automatický migrační nástroj chybí.
- Změnu zahrnout také do inicializace nových DB a [integrační fixture](../tests/Portal.IntegrationTests/ApiFixture.cs).

Výpis tabulek běžící DB:

```bash
docker compose exec db psql -U portal -d portal -c '\dt portal.*'
```
