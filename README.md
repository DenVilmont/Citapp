# Citapp MVP

## 1. Что есть в репозитории
- `src/Citapp.Admin` — Blazor WASM админка (frontend для владельца бизнеса).
- `src/Citapp.BotApi` — ASP.NET Core backend (Webhook + API для админки + бизнес-логика слотов/записей).
- Supabase — Postgres/Auth/Storage, используется обеими частями.

## 2. Локальный запуск
1. Установить .NET 10 SDK.
2. В Supabase применить миграции из раздела 6 (все файлы по порядку).
3. Настроить переменные окружения для BotApi (см. раздел 4).
4. Настроить `src/Citapp.Admin/wwwroot/appsettings.runtime.json` (см. раздел 5).
5. Запустить API: `dotnet run --project src/Citapp.BotApi`
6. Запустить Admin: `dotnet run --project src/Citapp.Admin`

## 3. WhatsApp webhook (Meta) + BotApi + Supabase
1. Поднимите BotApi локально/на сервере.
2. Укажите в Meta Webhook URL: `https://<botapi-domain>/webhook/whatsapp`.
3. Для локальной разработки можно использовать ngrok: `ngrok http 5001`, затем подставить `https://<ngrok-id>.ngrok.io/webhook/whatsapp`.
4. `WHATSAPP_VERIFY_TOKEN` в BotApi должен совпадать с verify token в Meta.

Итоговая связка: WhatsApp Cloud API → webhook в `Citapp.BotApi` → чтение/запись данных в Supabase; `Citapp.Admin` работает отдельно как UI и обращается к BotApi/Supabase.

## 4. Переменные окружения BotApi
| Variable | Description |
|---|---|
| SUPABASE_URL | URL Supabase |
| SUPABASE_SERVICE_ROLE_KEY | Secret service role key |
| SUPABASE_DB_CONNECTION_STRING | Строка подключения к Postgres (обязательна для репозиториев BotApi) |
| WHATSAPP_ACCESS_TOKEN | Meta token |
| WHATSAPP_VERIFY_TOKEN | Verify token |
| WHATSAPP_APP_SECRET | App secret for HMAC |
| CORS_ALLOWED_ORIGINS | Список origins через запятую (например: `http://localhost:5000,https://your-admin-domain`) |

> Примечание: если `CORS_ALLOWED_ORIGINS` не задан, в Development разрешены только `http://localhost:5000`, `https://localhost:5001`, `http://localhost:5173`, `https://localhost:5173`.

## 5. Runtime-конфиг для Admin (Blazor WASM)
Admin читает `wwwroot/appsettings.runtime.json` на старте, поэтому для статического хостинга (включая GitHub Pages) значения задаются без перекомпиляции.

Обязательные ключи:

```json
{
  "Supabase": {
    "Url": "https://<project-ref>.supabase.co",
    "AnonKey": "<public-anon-key>",
    "ServiceMediaBucket": "service-media"
  },
  "BotApi": {
    "BaseUrl": "https://<botapi-domain>"
  }
}
```

## 6. SQL-миграции
Применяйте файлы **строго по порядку** через SQL Editor Supabase:
1. `Supabase/Migrations/001_initial_schema.sql`
2. `Supabase/Migrations/002_rls_policies.sql`
3. `Supabase/Migrations/003_hardening_policies_constraints_storage.sql`
4. `Supabase/Migrations/004_allow_blocked_by_master_booking_status.sql`
5. `Supabase/Migrations/005_mvp_uniqueness_invariants.sql`

Коротко по статусам для занятости слотов (актуальная семантика):
- блокируют слот: `booked`, `blocked_by_master`;
- не блокируют слот: `completed`, `no_show`, `cancelled`.

## 7. Деплой Admin на GitHub Pages
Workflow `.github/workflows/deploy-admin.yml` публикует `src/Citapp.Admin` в GitHub Pages при push в `main` и генерирует `appsettings.runtime.json` из GitHub Actions Variables:

- `SUPABASE_URL`
- `SUPABASE_ANON_KEY`
- `SUPABASE_SERVICE_MEDIA_BUCKET`
- `BOTAPI_BASE_URL`
