# PostgresVaultService

Демонстрационный стек: **.NET 10 + Npgsql + PostgreSQL 18 + HashiCorp Vault + FreeIPA (AD/LDAP)** с автоматической почасовой ротацией пароля технологической учётной записи средствами **Vault Database Secrets Engine** и бесшовным переподключением сервиса.

## Архитектура

```mermaid
flowchart LR
    subgraph Identity
        IPA[FreeIPA<br/>LDAP/Kerberos]
    end

    subgraph Secrets
        V[HashiCorp Vault<br/>Database Engine + LDAP + AppRole]
    end

    subgraph Data
        PG[(PostgreSQL 18)]
    end

    subgraph App
        S[.NET 10 Worker<br/>Npgsql]
    end

    IPA -->|LDAP login для операторов| V
    V -->|static role rotation 1h<br/>ALTER USER| PG
    S -->|read database/static-creds| V
    S -->|INSERT heartbeat| PG
```

### Компоненты

| Сервис | Назначение |
|--------|------------|
| `freeipa` | AD-совместимый каталог (LDAP/Kerberos) для аутентификации операторов в Vault |
| `postgres` | PostgreSQL 18, БД `appdb`, технический пользователь `app_tech` |
| `vault` | Database Secrets Engine, static role `app-tech`, LDAP auth, AppRole |
| `vault-init` | Одноразовая инициализация Vault: database engine, static role, LDAP, AppRole |
| `app` | .NET Worker: читает credentials из Vault, выполняет регулярные INSERT |

### Ротация и бесшовное переподключение

1. **Vault Database Secrets Engine** (static role `app-tech`, `rotation_period=1h`):
   - генерирует новый пароль;
   - выполняет `ALTER USER app_tech WITH PASSWORD ...` в PostgreSQL;
   - сохраняет пароль внутри Vault (атомарный цикл, без внешнего ротатора).

2. **.NET сервис**:
   - читает `database/static-creds/app-tech` каждые 15 секунд;
   - при изменении пароля создаёт новый `NpgsqlDataSource`;
   - старый пул соединений выводится из эксплуатации с задержкой 60 секунд;
   - при ошибке аутентификации (`28P01`/`28000`) принудительно обновляет credentials и повторяет подключение;
   - **Resilience Pipeline** (Polly): retry + timeout для операций Vault и PostgreSQL.

## Быстрый старт

### Требования

- Docker 24+ и Docker Compose v2
- ≥ 4 GB RAM (FreeIPA требует ресурсов, первый запуск может занять 5–10 минут)
- Порты: `5432`, `8200`, `8389`, `8080`, `8443`

### Запуск

```bash
cp .env.example .env
docker compose up --build -d
```

Проверка статуса:

```bash
docker compose ps
docker compose logs -f app
docker compose logs -f vault-init
```

### Проверка вставок в PostgreSQL

```bash
docker compose exec postgres psql -U postgres -d appdb -c \
  "SELECT id, event_type, created_at FROM app_events ORDER BY id DESC LIMIT 10;"
```

### Проверка ротации

```bash
# Принудительная ротация через Vault
docker compose exec vault vault write -f database/rotate-role/app-tech

# Текущие credentials
docker compose exec vault vault read database/static-creds/app-tech

# Логи сервиса — должен появиться "PostgreSQL credentials refreshed"
docker compose logs --tail=50 app
```

## Интеграция с FreeIPA (AD)

Vault настроен на LDAP-аутентификацию через FreeIPA:

- **URL:** `ldap://freeipa:389`
- **Домен:** `demo.local`
- **Администратор:** `admin` / пароль из `FREEIPA_ADMIN_PASSWORD`

Вход оператора в Vault через LDAP:

```bash
export VAULT_ADDR=http://localhost:8200
docker compose exec vault vault login -method=ldap username=admin
# пароль: значение FREEIPA_ADMIN_PASSWORD (по умолчанию Secret123!)
```

Группы FreeIPA `admins` и `ipausers` получают политику `ldap-users` (чтение `database/static-creds/app-tech`).

Сервис приложения использует **AppRole** (машинная аутентификация) — это стандартная практика для автоматизированных workload.

## Локальная разработка (.NET)

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet restore PostgresVaultService.sln
dotnet build PostgresVaultService.sln
dotnet run --project src/PostgresVaultService
```

Переменные окружения для локального запуска (после `docker compose up`):

```bash
export Vault__Address=http://localhost:8200
export Vault__StaticRoleName=app-tech
export Vault__RoleId=$(docker compose exec vault cat /vault/init/app-role-id)
export Vault__SecretId=$(docker compose exec vault cat /vault/init/app-secret-id)
```

## Структура проекта

```
├── docker-compose.yml
├── .env.example
├── docker/
│   ├── postgres/init/01-init.sql
│   └── vault/                  # config, policies, init Dockerfile
├── scripts/vault-init.sh
└── src/PostgresVaultService/   # .NET 10 Worker
    ├── Services/
    │   ├── VaultSecretProvider.cs
    │   ├── DynamicPostgresConnectionFactory.cs
    │   └── InsertWorker.cs
    └── ...
```

## Настройка

| Переменная | По умолчанию | Описание |
|------------|--------------|----------|
| `POSTGRES_ADMIN_PASSWORD` | `postgres-admin-secret` | Пароль суперпользователя PostgreSQL (для Vault database config) |
| `FREEIPA_ADMIN_PASSWORD` | `Secret123!` | Пароль admin FreeIPA |
| `ROTATION_PERIOD` | `1h` | Период ротации static role в Vault |
| `Vault__PollIntervalSeconds` | `15` | Интервал опроса Vault (сек) |
| `Postgres__InsertIntervalSeconds` | `5` | Интервал INSERT (сек) |

Расписание ротации задаётся в `scripts/vault-init.sh` через `ROTATION_PERIOD` (или `rotation_schedule` для cron-формата в Vault).

## Остановка и очистка

```bash
docker compose down
# Полная очистка данных:
docker compose down -v
```

> При переходе со старой версии (KV + rotator) необходимо `docker compose down -v` для пересоздания Vault и PostgreSQL.

## Безопасность (production)

Это демонстрационный стек. Для production рекомендуется:

- включить TLS для Vault, PostgreSQL и LDAP;
- использовать Vault HA + auto-unseal (KMS/HSM);
- заменить file storage Vault на integrated storage (Raft) в HA-режиме;
- ограничить AppRole политиками least-privilege;
- настроить аудит Vault и PostgreSQL;
- использовать отдельный privileged-аккаунт PostgreSQL только для Vault (не `app_tech`).
