# Citapp MVP

## 1. Локальный запуск
1. Установить .NET 10 SDK.
2. Запустить API: `dotnet run --project src/Citapp.BotApi`
3. Запустить Admin: `dotnet run --project src/Citapp.Admin`

## 2. ngrok для webhook
- `ngrok http 5001`
- В Meta webhook указать `https://<ngrok-id>.ngrok.io/webhook/whatsapp`

## 3. Переменные окружения BotApi
| Variable | Description |
|---|---|
| SUPABASE_URL | URL Supabase |
| SUPABASE_ANON_KEY | Public anon key |
| SUPABASE_SERVICE_ROLE_KEY | Secret service role key |
| SUPABASE_JWT_SECRET | JWT secret |
| WHATSAPP_ACCESS_TOKEN | Meta token |
| WHATSAPP_VERIFY_TOKEN | Verify token |
| WHATSAPP_APP_SECRET | App secret for HMAC |
| DEFAULT_TIMEZONE | Default timezone |
| CORS_ALLOWED_ORIGIN | Admin origin |

## 4. SQL-миграции
Примените SQL-файлы из `Supabase/Migrations/001_initial_schema.sql` и `Supabase/Migrations/002_rls_policies.sql` через SQL Editor Supabase.

## 5. Деплой Admin на GitHub Pages
Workflow `.github/workflows/deploy-admin.yml` публикует `src/Citapp.Admin` в GitHub Pages при push в `main`.
