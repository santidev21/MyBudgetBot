# Backups and restore

A dump that has never been restored is not a backup. This document describes how
MyBudget-bot backs up PostgreSQL, how those backups are verified, and how to bring
the database back if the worst happens.

Targets: **RPO ≤ 24 h, RTO ≤ 30 min.** Docker persistence is not a backup; the
`mybudget_pg_data` volume is the live database, not a copy of it.

## What exists

| Piece | Where | Purpose |
|---|---|---|
| `scripts/backup.sh` | cron, nightly | Dump, verify readability, promote, off-site copy, prune |
| `scripts/verify-backup.sh` | cron, weekly | Restore a dump into a throwaway DB and sanity-check it |
| `scripts/restore.sh` | manual | Break-glass restore into the live database |
| `scripts/install-backup-cron.sh` | manual, once | Install the two cron entries above |
| `mybudget_pg_backups` volume | the database container at `/backups` | Dumps, separate from the data volume |
| `scripts/lib/backup-lib.sh` | sourced | Shared primitives for all of the above |

Dumps are made with `pg_dump --format=custom --no-owner --no-privileges`, so a
restore does not depend on the roles that created the objects.

## Nightly backup

`scripts/backup.sh`:

1. Dumps the live database into the `mybudget_pg_backups` volume.
2. Proves the dump is readable with `pg_restore --list`; an unreadable dump is a
   failure, not a warning.
3. Hardlinks the dump into the current ISO-week and calendar-month buckets. A
   hardlink shares the inode, so long-term retention costs no extra space.
4. Copies an `age`-encrypted version off-site, when configured.
5. Prunes old dumps.

Run it by hand any time:

```bash
cd /opt/mybudget
DEPLOY_DIR=/opt/mybudget ./scripts/backup.sh
```

### Retention

| Bucket | Default | Environment variable |
|---|---|---|
| Daily | 7 | `BACKUP_RETENTION_DAYS` |
| Weekly | 4 | `BACKUP_WEEKLY_RETENTION` |
| Monthly | 12 | `BACKUP_MONTHLY_RETENTION` |

That is the classic 7 daily / 4 weekly / 12 monthly rotation.

### Off-site copies

Backups that only exist on the VPS are lost with the VPS. To enable encrypted
off-site copies, install [`age`](https://github.com/FiloSottile/age) on the host and
set, in `.env`:

```
BACKUP_OFFSITE_TARGET=/mnt/offsite/mybudget
BACKUP_AGE_RECIPIENT=age1...your-public-key...
```

Every dump is encrypted with the recipient's public key before it is written to the
target, so the storage provider never sees plaintext. Keep the private key off the
VPS; without it the off-site copies cannot be read, which is the point. Check the
off-site copy periodically by decrypting it on a different machine and listing it
with `pg_restore --list` (see "Restoring an off-site copy" below); a copy nobody has
ever decrypted is not verified.

## Restore drill (automatic verification)

`scripts/verify-backup.sh` is what actually proves a backup is usable. It:

1. Picks the newest daily dump (or one passed by name).
2. Creates the throwaway `mybudget_restore_check` database.
3. Restores the dump with `pg_restore --exit-on-error`.
4. Runs sanity checks: migration history present, no expense with a non-positive
   amount, no expense without a calendar date, no user without a Telegram id.
5. Drops the throwaway database. The live database is never touched.

Any failure sends a Telegram alert to `ADMIN_TELEGRAM_USER_ID` and exits non-zero,
so cron mail or the monitoring wrapper notices too.

```bash
cd /opt/mybudget
DEPLOY_DIR=/opt/mybudget ./scripts/verify-backup.sh
DEPLOY_DIR=/opt/mybudget ./scripts/verify-backup.sh mybudget-20260928-191226.dump
```

A passing run looks like:

```
Restore drill for mybudget-20260928-191226.dump passed:
  users=1
  expenses=1
  newest_expense_date=2026-09-28
```

## Scheduling

Install the cron entries once, as the user that can run Docker:

```bash
cd /opt/mybudget
./scripts/install-backup-cron.sh
```

That writes an idempotent managed block:

```
# BEGIN mybudget backups (managed)
15 3 * * * /opt/mybudget/scripts/backup.sh >> /opt/mybudget/logs/backup.log 2>&1
15 4 * * 0 /opt/mybudget/scripts/verify-backup.sh >> /opt/mybudget/logs/verify.log 2>&1
# END mybudget backups
```

Override the schedule with `BACKUP_SCHEDULE` and `VERIFY_SCHEDULE` if the defaults
collide with other jobs. Inspect the installed block with `crontab -l`.

## Restore runbook

Use this only when the live database must be replaced. A schema-only rollback does
not need it: `scripts/deploy.sh rollback` returns to the previous image and the
migrator handles the schema.

### Preferred: the script

```bash
cd /opt/mybudget
CONFIRM_RESTORE=RESTORE ./scripts/restore.sh mybudget-20260928-191226.dump
CONFIRM_RESTORE=RESTORE ./scripts/restore.sh --latest
```

`restore.sh`:

1. Refuses to run without `CONFIRM_RESTORE=RESTORE`.
2. Stops the application and the migrator so nothing is writing.
3. Drops and recreates the database owned by `mybudget_migrator`.
4. Restores the dump **as the migrator role**, so table ownership matches the
   role model the migrator expects.
5. Re-applies the least-privilege grants (`mybudget_app` gets DML only) because the
   dump is deliberately taken with `--no-owner --no-privileges`.
6. Brings the stack back up and waits for the health check.

### Manual fallback

If the script cannot run, the same steps by hand:

```bash
cd /opt/mybudget
docker stop mybudget
docker rm -f mybudget-migrator
docker exec mybudget-db dropdb --if-exists --force -U <POSTGRES_USER> <POSTGRES_DB>
docker exec mybudget-db createdb -O <DB_MIGRATOR_USER> -U <POSTGRES_USER> <POSTGRES_DB>
docker exec mybudget-db pg_restore --no-owner --no-privileges --exit-on-error \
  -U <DB_MIGRATOR_USER> -d <POSTGRES_DB> /backups/<dump>
# Re-apply the grants (see restore.sh's reapply_grants or docker/postgres/init).
docker compose up -d --remove-orphans
```

Then verify with `./scripts/deploy.sh verify` and check a known expense in `📋 Gastos`.

### Restoring an off-site copy

Decrypt it first with the private key that never lives on the VPS:

```bash
age --decrypt --identity /secure/backup.key -o /tmp/restored.dump backup.dump.age
docker cp /tmp/restored.dump mybudget-db:/backups/restored.dump
CONFIRM_RESTORE=RESTORE ./scripts/restore.sh restored.dump
```

## Checklist after any restore

- [ ] `./scripts/deploy.sh verify` reports the readiness check passing.
- [ ] `/health` shows the `postgres` check healthy.
- [ ] `📊 Resumen` shows the expected month total.
- [ ] `📋 Gastos` shows a known recent expense with the right date and amount.
- [ ] The migrator container exited 0 (`docker ps -a | grep mybudget-migrator`).

## Drill log

Record every drilled restore here. A backup that was not restored in the last month
should be treated as unverified.

| Date | Dump | Result | Notes |
|---|---|---|---|
| 2026-09-28 | `mybudget-20260928-191226.dump` | passed | Local drill: restore into `mybudget_restore_check`, sanity checks green (`users=1, expenses=1, newest_expense_date=2026-09-28`); corrupt 0-byte dump correctly failed and dropped the check database. |
