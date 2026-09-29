# Deployment

How MyBudget-bot reaches the VPS, and how to take it back if a release goes wrong.
The architecture is in [`TECHNICAL-DESIGN.md`](TECHNICAL-DESIGN.md) §15; backups and
restores are in [`BACKUPS.md`](BACKUPS.md).

## Topology

```
Telegram ──HTTPS──▶ vps-gateway (nginx :443)
                        │ proxy_pass http://mybudget:8080
                        ▼
                   mybudget          (mybudget-net + mybudget-internal-net)
                        │
                   mybudget-db       (mybudget-internal-net only)
```

No application or database port is published in production. Only the gateway is
public, and only `/health/live` is reachable without the private network ranges.

## First deploy

1. Create the DNS record and add `mybudget-net` to the gateway compose.
2. Clone the repository to `/opt/mybudget` and create `.env` from `.env.example`:

   ```bash
   cd /opt/mybudget
   cp .env.example .env
   ./scripts/init-telegram-env.sh --polling=false
   ```

   Never commit `.env`. CI asserts it is untracked and ignored.

3. Issue the certificate with certbot, copy
   `docs/gateway/mybudget.santidev21.tech.conf` into the gateway's `sites-enabled/`,
   then `nginx -t && nginx -s reload`.
4. Deploy and register the webhook:

   ```bash
   ./scripts/deploy.sh deploy
   dotnet run --project src/MyBudget.Api -- --configure-telegram
   ```

5. Install the backup schedule once:

   ```bash
   ./scripts/install-backup-cron.sh
   ```

## Regular deploy

CI runs build, tests, formatting, security scans, a Docker build and a Trivy scan,
then over SSH runs `scripts/deploy.sh deploy` on the VPS. The same command works by
hand.

`deploy.sh deploy` is ordered so a failure is always recoverable:

1. Validate Docker, `.env`, the compose file and the working tree.
2. **Back up** the database into the `mybudget_pg_backups` volume and verify the dump
   with `pg_restore --list`. The deploy refuses to continue if the backup fails.
3. **Pull a clean checkout**: `git reset --hard origin/main` plus `git clean -fd`,
   then assert `git status --porcelain` is empty. Ignored files such as `.env` are
   preserved on purpose.
4. Build images with `--no-cache`.
5. Start the stack, waiting for the one-shot migrator to succeed.
6. Verify the readiness endpoint; roll back if it does not answer.

### Why "clean checkout" matters

The build context is the checkout. A stray untracked file (a shadowing source file,
a modified compose override) would silently change what ships. Resetting tracked
files and removing untracked ones before the build is what makes a deploy from the
VPS reproducible from a commit.

## Rollback

```bash
cd /opt/mybudget
./scripts/deploy.sh rollback
```

It resets to `HEAD~1`, rebuilds and restarts. The pre-deploy dump from the failed
deploy is named in the log; restore it only if the previous release needs the older
schema:

```bash
CONFIRM_RESTORE=RESTORE ./scripts/restore.sh <pre-deploy-dump>
```

## Operations commands

| Command | Effect |
|---|---|
| `./scripts/deploy.sh deploy` | Validated, backed-up, clean deploy |
| `./scripts/deploy.sh check` | Run the pre-deploy validations without building |
| `./scripts/deploy.sh status` | Container states |
| `./scripts/deploy.sh logs` | Follow application logs |
| `./scripts/deploy.sh verify` | Check health endpoints |
| `./scripts/deploy.sh rollback` | Return to the previous commit |
| `./scripts/backup.sh` | Run a nightly backup now |
| `./scripts/verify-backup.sh` | Run the restore drill now |

## Secrets and roles

- Secrets live only in `.env` or the environment. Serilog redacts bot tokens,
  passwords, connection strings and `Authorization`.
- `mybudget_migrator` owns the schema and may run DDL; only the one-shot migrator
  uses it. `mybudget_app` is DML-only and is what the application uses.
- `docker/postgres/init/01-create-users.sh` runs only on an empty data volume.
  Changing a password there later has no effect: rotate with `ALTER ROLE`.

## Troubleshooting

| Symptom | Check |
|---|---|
| `docker compose config` fails | Required variables missing from `.env` |
| Migrator exits non-zero | Migration error; the app never starts, by design |
| Readiness never turns healthy | `./scripts/deploy.sh logs`; database reachable over the internal network |
| Webhook returns 404 | `--configure-telegram` was not run, or the secret path changed |
| Telegram 429 in logs | Expected; the sender honours `retry_after` and retries |
| Backups missing | `crontab -l`, and the drill in `BACKUPS.md` |

The application never migrates on start, so a failed migration is loud rather than a
half-booted service. Only `/health/live` is used for the container health check, so a
database outage never triggers a restart loop.
