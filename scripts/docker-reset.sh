#!/usr/bin/env bash
# Mirats / AspireReact — Reset the Docker Compose production stack data.
#
# SAFETY:
#  - Removes ONLY the Docker Compose production volumes, i.e. those named `mirats-*`
#    (mirats-postgres-data, mirats-redis-data, mirats-keycloak-data).
#  - NEVER touches the Aspire dev volumes (`postgres-data`, `keycloak-data` — no
#    `mirats-` prefix). Those are the dev stack's volumes and are left untouched.
#  - Asks for confirmation before deleting anything — this is IRREVERSIBLE.
#
# Usage (run from anywhere; it resolves the repo root itself):
#   bash scripts/docker-reset.sh
set -euo pipefail

# Resolve repo root = parent of the scripts/ directory.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

if ! command -v docker >/dev/null 2>&1; then
  echo "ERROR: docker not found on PATH." >&2
  exit 1
fi

echo "== Mirats Docker Compose stack reset =="
echo "Repo root: $REPO_ROOT"
echo

# 1. Collect the production volumes we are allowed to remove (mirats-* prefix).
BEFORE=""
while IFS= read -r line; do
  [ -n "$line" ] && BEFORE="$BEFORE $line"
done < <(docker volume ls -q --filter name=^mirats- 2>/dev/null || true)

COMPOSE_FILE="docker-compose.yml"
if [ -f "$COMPOSE_FILE" ]; then
  echo "Compose file: present ($COMPOSE_FILE)"
else
  echo "Compose file: MISSING ($COMPOSE_FILE) — volumes will still be cleaned"
fi
echo

if [ -z "$BEFORE" ]; then
  echo "No existing 'mirats-*' volumes found right now."
  echo "  -> docker compose down -v will still be run (removes any compose-managed volumes)."
else
  echo "The following volumes will be PERMANENTLY DELETED:"
  for v in $BEFORE; do
    echo "  - $v"
  done
fi

# Informational guard: show the Aspire dev volumes we will NOT touch.
DEV_PG=$(docker volume ls -q --filter name=^postgres-data$ 2>/dev/null || true)
DEV_KC=$(docker volume ls -q --filter name=^keycloak-data$ 2>/dev/null || true)
DEV_VOLUMES=$(printf '%s %s' "$DEV_PG" "$DEV_KC")
echo "Aspire dev volumes (NOT touched): ${DEV_VOLUMES:-none}"
echo

# 2. Confirmation — never delete silently.
read -r -p "WARNING: This deletes ALL data of the Docker Compose production stack. This CANNOT be undone. Type 'yes' to continue: " ANSWER
ANSWER_LC=$(echo "$ANSWER" | tr '[:upper:]' '[:lower:]')
if [ "$ANSWER_LC" != "yes" ]; then
  echo "Aborted. No volumes were removed."
  exit 1
fi

echo
echo "Running: docker compose down -v ..."
DOWN_OK=0
if docker compose down -v; then
  DOWN_OK=1
else
  echo "WARN: 'docker compose down -v' failed (usually missing/empty required vars in .env)." >&2
  echo "  Falling back to manual cleanup of the compose project's containers." >&2
fi

# 3. Fallback: if compose could not run (no .env / interpolation error),
#    remove the compose project's containers directly via its label.
if [ "$DOWN_OK" -ne 1 ]; then
  PROJECT="$(basename "$REPO_ROOT" | tr '[:upper:]' '[:lower:]' | sed 's/[^a-z0-9_.-]//g')"
  CONTAINERS="$(docker ps -aq --filter "label=com.docker.compose.project=$PROJECT" 2>/dev/null || true)"
  if [ -n "$CONTAINERS" ]; then
    echo "Removing compose containers for project '$PROJECT':"
    # shellcheck disable=SC2086
    docker rm -f $CONTAINERS
  else
    echo "No running containers found for project '$PROJECT'."
  fi
fi

# 4. Safety net: remove any leftover mirats-* volumes not removed by compose.
LEFT=""
while IFS= read -r line; do
  [ -n "$line" ] && LEFT="$LEFT $line"
done < <(docker volume ls -q --filter name=^mirats- 2>/dev/null || true)

if [ -n "$LEFT" ]; then
  echo "Removing leftover mirats-* volumes:"
  for v in $LEFT; do
    echo "  - $v"
  done
  # shellcheck disable=SC2086
  docker volume rm $LEFT
else
  echo "No leftover mirats-* volumes."
fi

echo
echo "=== DONE. Production stack data has been reset. ==="
echo
echo "To rebuild from scratch:"
echo "  1) cp .env.example .env   (fill in all REQUIRED vars)"
echo "  2) docker compose up -d --build"
echo "  3) bash scripts/seed-initial-admin.sh   (or: scripts/seed-initial-admin.ps1 on Windows)"
echo
echo "Aspire dev volumes (postgres-data, keycloak-data) are untouched."
