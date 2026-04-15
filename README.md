# Citapp MVP

## 1. Локальный запуск
1. Установить .NET 10 SDK.
2. Настроить переменные окружения для BotApi (см. раздел 3).
3. Настроить `src/Citapp.Admin/wwwroot/appsettings.runtime.json` (см. раздел 4).
4. Запустить API: `dotnet run --project src/Citapp.BotApi`
5. Запустить Admin: `dotnet run --project src/Citapp.Admin`

## 2. ngrok для webhook
- `ngrok http 5001`
- В Meta webhook указать `https://<ngrok-id>.ngrok.io/webhook/whatsapp`

## 3. Переменные окружения BotApi
| Variable | Description |
|---|---|
| SUPABASE_URL | URL Supabase |
| SUPABASE_SERVICE_ROLE_KEY | Secret service role key |
| WHATSAPP_ACCESS_TOKEN | Meta token |
| WHATSAPP_VERIFY_TOKEN | Verify token |
| WHATSAPP_APP_SECRET | App secret for HMAC |
| CORS_ALLOWED_ORIGINS | Список origins через запятую (например: `http://localhost:5000,https://your-admin-domain`) |

> Примечание: если `CORS_ALLOWED_ORIGINS` не задан, в Development разрешены только `http://localhost:5000`, `https://localhost:5001`, `http://localhost:5173`, `https://localhost:5173`.

## 4. Runtime-конфиг для Admin (Blazor WASM)
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

## 5. SQL-миграции
Примените SQL-файлы из `Supabase/Migrations/001_initial_schema.sql` и `Supabase/Migrations/002_rls_policies.sql` через SQL Editor Supabase.

## 6. Деплой Admin на GitHub Pages
Workflow `.github/workflows/deploy-admin.yml` публикует `src/Citapp.Admin` в GitHub Pages при push в `main` и генерирует `appsettings.runtime.json` из GitHub Actions Variables:

- `SUPABASE_URL`
- `SUPABASE_ANON_KEY`
- `SUPABASE_SERVICE_MEDIA_BUCKET`
- `BOTAPI_BASE_URL`
