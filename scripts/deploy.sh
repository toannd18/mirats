#!/usr/bin/env bash
# Mirats / AspireReact - safe deploy/upgrade script (Linux).
#
# Order (FAIL-CLOSED):
#   1. encrypted backup via the `backup` service (--once)
#   2. build/pull images
#   3. docker compose up -d
#   4. wait for /api/v1/health/ready == 200 (timeout)
#   5. on failure: print rollback instructions (restore + previous commit)
#
# The new version is NEVER started when the pre-deploy backup fails.
#
# Usage: ./scripts/deploy.sh
# Env:
#   BACKEND_PORT            host port of the API (default 5000)
#   DEPLOY_HEALTH_URL       override readiness URL
#   DEPLOY_HEALTH_TIMEOUT   seconds to wait (default 180)

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

COMPOSE="${COMPOSE:-docker compose}"
BACKEND_PORT="${BACKEND_PORT:-5000}"
HEALTH_URL="${DEPLOY_HEALTH_URL:-http://127.0.0.1:${BACKEND_PORT}/api/v1/health/ready}"
HEALTH_TIMEOUT="${DEPLOY_HEALTH_TIMEOUT:-180}"
BACKUP_SERVICE="${BACKUP_SERVICE:-backup}"

previous_commit="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"

log() { printf '[deploy] %s\n' "$*"; }

print_rollback() {
    cat <<EOF

================ ROLLBACK ================
The new version did NOT become ready. The old containers may still be running,
but the database may already have been migrated by the new server - restore first.

1) Restore the latest encrypted backup (full runbook: docs/RUNBOOK_BACKUP_RESTORE.md):
     cd $(pwd)
     docker compose stop server frontend backup
     # decrypt on the admin machine (private key must NOT be on the server):
     age -d -i ./backup-key.txt backup-data/aspire-react-db-<timestamp>.dump.age > /tmp/restore.dump
     docker compose exec -T postgres dropdb   -U postgres --if-exists aspire-react-db
     docker compose exec -T postgres createdb -U postgres aspire-react-db
     cat /tmp/restore.dump | docker compose exec -T postgres pg_restore -U postgres -d aspire-react-db --clean --if-exists --no-owner
     docker compose start server frontend backup
     curl -fsS ${HEALTH_URL}

2) Return to the previous code/image and rebuild:
     git checkout ${previous_commit}
     docker compose up -d --build

3) Verify:
     curl -fsS ${HEALTH_URL}          # expect 200
     docker compose ps                # server healthy
     # log in and spot-check data

If the backup itself failed, nothing was built/started by this script - fix the
backup first (docker compose logs ${BACKUP_SERVICE}) and rerun.
==========================================
EOF
}

log "pre-deploy backup (service: ${BACKUP_SERVICE})..."
if ! ${COMPOSE} run --rm --no-deps "${BACKUP_SERVICE}" --once; then
    log "BACKUP FAILED - aborting. Nothing was built or started; the old version keeps running."
    log "Inspect: ${COMPOSE} logs ${BACKUP_SERVICE}"
    exit 1
fi
log "backup OK; last_success updated"

log "building images (pull base images)..."
if ! ${COMPOSE} build --pull; then
    log "BUILD FAILED - aborting before 'up'. Old version still running."
    exit 1
fi

log "starting stack..."
${COMPOSE} up -d --remove-orphans

log "waiting for readiness: ${HEALTH_URL} (timeout ${HEALTH_TIMEOUT}s)..."
deadline=$(( $(date +%s) + HEALTH_TIMEOUT ))
last_code=""
while true; do
    last_code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "${HEALTH_URL}" 2>/dev/null || true)"
    if [ "${last_code}" = "200" ]; then
        log "deploy OK: /api/v1/health/ready = 200 (previous commit: ${previous_commit})"
        exit 0
    fi
    if [ "$(date +%s)" -ge "${deadline}" ]; then
        log "READINESS FAILED after ${HEALTH_TIMEOUT}s (last HTTP status: ${last_code:-no response})"
        print_rollback
        exit 1
    fi
    sleep 3
done
