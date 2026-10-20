#!/usr/bin/env bash
# Mirats / AspireReact - daily encrypted Postgres backup.
#
# Runs as PID 1 in the `backup` sidecar (official postgres base + age/rsync).
# `--once` runs a single backup and exits with 0/1 (used by deploy.sh and manual drills).
#
# Security model:
#   - BACKUP_AGE_RECIPIENT is the PUBLIC age key; the PRIVATE key never exists on the server.
#   - The plaintext dump is deleted immediately after successful encryption.
#   - last_success is updated ONLY after dump + encryption + checksum (+ optional remote copy) succeed.

set -uo pipefail

BACKUP_DIR="${BACKUP_DIR:-/backups}"
BACKUP_STATE_FILE="${BACKUP_STATE_FILE:-$BACKUP_DIR/last_success}"
BACKUP_TIMEZONE="${BACKUP_TIMEZONE:-Asia/Ho_Chi_Minh}"
BACKUP_SCHEDULE_HOUR="${BACKUP_SCHEDULE_HOUR:-1}"
BACKUP_SCHEDULE_MINUTE="${BACKUP_SCHEDULE_MINUTE:-0}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-30}"
BACKUP_REMOTE="${BACKUP_REMOTE:-}"
BACKUP_REMOTE_SSH_KEY="${BACKUP_REMOTE_SSH_KEY:-}"
BACKUP_DB_NAME="${BACKUP_DB_NAME:-aspire-react-db}"

export TZ="$BACKUP_TIMEZONE"

PGHOST="${PGHOST:-postgres}"
PGPORT="${PGPORT:-5432}"
PGUSER="${PGUSER:-postgres}"
PGDATABASE="${PGDATABASE:-$BACKUP_DB_NAME}"

log() {
    printf '%s [backup] %s\n' "$(date '+%Y-%m-%dT%H:%M:%S%z')" "$*"
}

fail() {
    printf '%s [backup] ERROR %s\n' "$(date '+%Y-%m-%dT%H:%M:%S%z')" "$*" >&2
}

validate_config() {
    if [ -z "${BACKUP_AGE_RECIPIENT:-}" ]; then
        fail "BACKUP_AGE_RECIPIENT is empty - refusing to write an unencrypted backup."
        exit 2
    fi

    case "$BACKUP_SCHEDULE_HOUR" in
        ''|*[!0-9]*) fail "BACKUP_SCHEDULE_HOUR must be 0-23 (got '$BACKUP_SCHEDULE_HOUR')"; exit 2 ;;
    esac
    case "$BACKUP_SCHEDULE_MINUTE" in
        ''|*[!0-9]*) fail "BACKUP_SCHEDULE_MINUTE must be 0-59 (got '$BACKUP_SCHEDULE_MINUTE')"; exit 2 ;;
    esac
    if [ "$BACKUP_SCHEDULE_HOUR" -gt 23 ] || [ "$BACKUP_SCHEDULE_MINUTE" -gt 59 ]; then
        fail "Invalid schedule ${BACKUP_SCHEDULE_HOUR}:${BACKUP_SCHEDULE_MINUTE} (expected HH 0-23 / MM 0-59)"
        exit 2
    fi
    case "$BACKUP_RETENTION_DAYS" in
        ''|*[!0-9]*) fail "BACKUP_RETENTION_DAYS must be a positive integer"; exit 2 ;;
    esac
    if [ "$BACKUP_RETENTION_DAYS" -lt 1 ]; then
        fail "BACKUP_RETENTION_DAYS must be >= 1"
        exit 2
    fi

    mkdir -p "$BACKUP_DIR"
}

# Returns 0 only when the full backup pipeline succeeded. Never updates last_success on failure.
run_backup() {
    local timestamp plain encrypted checksum filesize
    timestamp="$(date '+%Y%m%d-%H%M%S')"
    plain="$BACKUP_DIR/${BACKUP_DB_NAME}-${timestamp}.dump"
    encrypted="${plain}.age"
    checksum="${encrypted}.sha256"

    log "starting: database=$PGDATABASE host=$PGHOST:$PGPORT -> $encrypted"

    if ! pg_dump -Fc -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -f "$plain"; then
        fail "pg_dump failed; partial file removed, last_success NOT updated"
        rm -f "$plain"
        return 1
    fi

    if ! age -r "$BACKUP_AGE_RECIPIENT" -o "$encrypted" "$plain"; then
        fail "age encryption failed; files removed, last_success NOT updated"
        rm -f "$plain" "$encrypted"
        return 1
    fi

    # Plaintext must not remain on disk after encryption.
    rm -f "$plain"

    if ! ( cd "$BACKUP_DIR" && sha256sum "$(basename "$encrypted")" > "$(basename "$checksum")" ); then
        fail "sha256sum failed; last_success NOT updated"
        return 1
    fi
    chmod 600 "$encrypted" "$checksum"

    # Retention: delete encrypted dumps + checksums older than N days.
    find "$BACKUP_DIR" -maxdepth 1 -type f -name "${BACKUP_DB_NAME}-*.dump.age" -mtime +"$BACKUP_RETENTION_DAYS" -print -delete \
        | while IFS= read -r old; do log "retention: removed $old"; done
    find "$BACKUP_DIR" -maxdepth 1 -type f -name "${BACKUP_DB_NAME}-*.dump.age.sha256" -mtime +"$BACKUP_RETENTION_DAYS" -delete

    # Optional off-site copy. Disabled by default (BACKUP_REMOTE empty).
    if [ -n "$BACKUP_REMOTE" ]; then
        local ssh_opts=(-o BatchMode=yes -o StrictHostKeyChecking=accept-new)
        if [ -n "$BACKUP_REMOTE_SSH_KEY" ]; then
            ssh_opts+=(-i "$BACKUP_REMOTE_SSH_KEY")
        fi
        if ! rsync -az --timeout=300 -e "ssh ${ssh_opts[*]}" "$encrypted" "$checksum" "${BACKUP_REMOTE%/}/"; then
            fail "remote copy to '$BACKUP_REMOTE' failed; last_success NOT updated"
            return 1
        fi
        log "remote copy OK -> $BACKUP_REMOTE"
    fi

    filesize="$(stat -c '%s' "$encrypted")"
    # Atomic state update - the ONLY place last_success changes.
    if ! ( date '+%Y-%m-%dT%H:%M:%S%:z' > "${BACKUP_STATE_FILE}.tmp" && mv "${BACKUP_STATE_FILE}.tmp" "$BACKUP_STATE_FILE" ); then
        fail "could not write state file $BACKUP_STATE_FILE"
        return 1
    fi
    chmod 644 "$BACKUP_STATE_FILE"

    log "SUCCESS: $encrypted (${filesize} bytes), checksum ${checksum}, last_success updated"
    return 0
}

trap 'log "termination signal received; exiting scheduler"; exit 0' TERM INT

case " $* " in
    *" --once "*) once=1 ;;
    *) once=0 ;;
esac

validate_config

if [ "$once" -eq 1 ]; then
    run_backup
    exit $?
fi

log "scheduler started: daily at $(printf '%02d:%02d' "$BACKUP_SCHEDULE_HOUR" "$BACKUP_SCHEDULE_MINUTE") $TZ; retention ${BACKUP_RETENTION_DAYS}d; remote copy: ${BACKUP_REMOTE:-disabled}"

while true; do
    now="$(date +%s)"
    next="$(date -d "today $(printf '%02d:%02d' "$BACKUP_SCHEDULE_HOUR" "$BACKUP_SCHEDULE_MINUTE")" +%s)"
    if [ "$next" -le "$now" ]; then
        next="$(date -d "tomorrow $(printf '%02d:%02d' "$BACKUP_SCHEDULE_HOUR" "$BACKUP_SCHEDULE_MINUTE")" +%s)"
    fi
    delay=$((next - now))
    log "next backup at $(date -d "@$next" '+%Y-%m-%d %H:%M:%S %Z') (in ${delay}s)"
    sleep "$delay" &
    wait $!
    run_backup || fail "backup run failed; last_success NOT updated"
done
