#!/usr/bin/env bash
# Seed the initial Mirats app admin (INITIAL_ADMIN_*) via Keycloak Admin API (Phương án A).
# JIT-provisioning creates the local DB User with IsSuperUser=true on first login.
# Fails fast if INITIAL_ADMIN_* missing or Keycloak not ready.
#
# Usage:
#   export KEYCLOAK_PUBLIC_URL=http://localhost:8080   # preferred (browser/host reachable)
#   export INITIAL_ADMIN_USERNAME=admin INITIAL_ADMIN_EMAIL=admin@example.com INITIAL_ADMIN_PASSWORD='ChangeMe!'
#   bash scripts/seed-initial-admin.sh
#   # Or: KEYCLOAK_URL=... REALM=aspire-react bash scripts/seed-initial-admin.sh
set -euo pipefail

KEYCLOAK_URL="${KEYCLOAK_PUBLIC_URL:-${KEYCLOAK_URL:-${KEYCLOAK_SERVER_URL:-http://localhost:8080}}}"
KEYCLOAK_URL="${KEYCLOAK_URL%/}"
REALM="${KEYCLOAK_REALM:-aspire-react}"
MASTER_REALM="master"

# Auto-enable -k for self-signed HTTPS on localhost/127.0.0.1 (Aspire dev). Docker HTTP is unaffected.
CURL_INSECURE=""
if [[ "$KEYCLOAK_URL" =~ ^https://(localhost|127\.0\.0\.1)([:/]|$) ]]; then
  CURL_INSECURE="-k"
fi
# Allow explicit override: SKIP_CERT_CHECK=1 forces -k, =0 forces no -k.
if [[ "${SKIP_CERT_CHECK:-}" == "1" ]]; then CURL_INSECURE="-k"
elif [[ "${SKIP_CERT_CHECK:-}" == "0" ]]; then CURL_INSECURE=""
fi

: "${INITIAL_ADMIN_USERNAME:?INITIAL_ADMIN_USERNAME is required — set it in .env (no default, no Admin123! fallback).}"
: "${INITIAL_ADMIN_EMAIL:?INITIAL_ADMIN_EMAIL is required — set it in .env.}"
: "${INITIAL_ADMIN_PASSWORD:?INITIAL_ADMIN_PASSWORD is required — set it in .env (no default, no Admin123! fallback).}"
: "${KC_BOOTSTRAP_ADMIN_USERNAME:?KC_BOOTSTRAP_ADMIN_USERNAME is required — set it in .env (Keycloak master admin).}"
: "${KC_BOOTSTRAP_ADMIN_PASSWORD:?KC_BOOTSTRAP_ADMIN_PASSWORD is required — set it in .env.}"

echo "Seeding initial admin '$INITIAL_ADMIN_USERNAME' ($INITIAL_ADMIN_EMAIL) into realm '$REALM' via $KEYCLOAK_URL ..."

# Obtain master admin token (retry — Keycloak may still be starting)
TOKEN=""
for i in $(seq 1 12); do
  if TOKEN=$(curl $CURL_INSECURE -fsS -X POST "$KEYCLOAK_URL/realms/$MASTER_REALM/protocol/openid-connect/token" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    --data-urlencode "grant_type=password" \
    --data-urlencode "client_id=admin-cli" \
    --data-urlencode "username=$KC_BOOTSTRAP_ADMIN_USERNAME" \
    --data-urlencode "password=$KC_BOOTSTRAP_ADMIN_PASSWORD" 2>&1 | python3 -c "import sys,json; print(json.load(sys.stdin).get('access_token',''))" 2>/dev/null); then
    if [ -n "$TOKEN" ] && [ "$TOKEN" != "" ]; then
      echo "  Admin token obtained."
      break
    fi
  fi
  echo "  Attempt $i/12: token request failed — retrying in 5s..."
  if [ "$i" -eq 12 ]; then
    echo "ERROR: Failed to obtain Keycloak admin token after 12 attempts. Is Keycloak ready at $KEYCLOAK_URL?" >&2
    exit 1
  fi
  sleep 5
done

auth_header="Authorization: Bearer $TOKEN"

# Check if user already exists
ENCODED_USER=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$INITIAL_ADMIN_USERNAME'))")
EXISTING=$(curl $CURL_INSECURE -fsS -H "$auth_header" "$KEYCLOAK_URL/admin/realms/$REALM/users?username=$ENCODED_USER&exact=true" 2>&1 || true)
COUNT=$(echo "$EXISTING" | python3 -c "import sys,json; d=json.load(sys.stdin); print(len(d) if isinstance(d,list) else 0)" 2>/dev/null || echo 0)
if [ "$COUNT" -gt 0 ] 2>/dev/null; then
  echo "  User '$INITIAL_ADMIN_USERNAME' already exists in realm '$REALM' — skipping creation (idempotent)."
  echo "  Done. The user can log in; JIT will ensure the local DB record on first login."
  exit 0
fi

# Create user
USER_ID=$(curl $CURL_INSECURE -fsS -i -X POST "$KEYCLOAK_URL/admin/realms/$REALM/users" \
  -H "$auth_header" -H "Content-Type: application/json" \
  -d "{\"username\":\"$INITIAL_ADMIN_USERNAME\",\"email\":\"$INITIAL_ADMIN_EMAIL\",\"enabled\":true,\"emailVerified\":true,\"firstName\":\"System\",\"lastName\":\"Admin\"}" 2>&1 \
  | grep -i "^Location:" | sed 's/.*\/\([^\/]*\)\r/\1/' | tr -d '\r\n' || true)

if [ -z "$USER_ID" ]; then
  # Fallback: search again
  sleep 1
  USER_ID=$(curl $CURL_INSECURE -fsS -H "$auth_header" "$KEYCLOAK_URL/admin/realms/$REALM/users?username=$ENCODED_USER&exact=true" \
    | python3 -c "import sys,json; d=json.load(sys.stdin); print(d[0]['id'] if d else '')" 2>/dev/null || true)
fi

if [ -z "$USER_ID" ]; then
  echo "ERROR: Failed to create user '$INITIAL_ADMIN_USERNAME' or retrieve its id." >&2
  exit 1
fi
echo "  User '$INITIAL_ADMIN_USERNAME' created (id: $USER_ID)."

# Reset password
curl $CURL_INSECURE -fsS -X PUT "$KEYCLOAK_URL/admin/realms/$REALM/users/$USER_ID/reset-password" \
  -H "$auth_header" -H "Content-Type: application/json" \
  -d "{\"type\":\"password\",\"value\":\"$INITIAL_ADMIN_PASSWORD\",\"temporary\":false}" >/dev/null
echo "  Password set for '$INITIAL_ADMIN_USERNAME'."

# Assign realm role 'admin'
ROLES_JSON=$(curl $CURL_INSECURE -fsS -H "$auth_header" "$KEYCLOAK_URL/admin/realms/$REALM/roles" 2>&1 || true)
ADMIN_ROLE_ID=$(echo "$ROLES_JSON" | python3 -c "import sys,json; roles=json.load(sys.stdin); print(next((r['id'] for r in roles if r.get('name')=='admin'),''))" 2>/dev/null || true)
if [ -z "$ADMIN_ROLE_ID" ]; then
  echo "  Warning: realm role 'admin' not found in '$REALM' — skipping role assignment."
else
  curl $CURL_INSECURE -fsS -X POST "$KEYCLOAK_URL/admin/realms/$REALM/users/$USER_ID/role-mappings/realm" \
    -H "$auth_header" -H "Content-Type: application/json" \
    -d "[{\"id\":\"$ADMIN_ROLE_ID\",\"name\":\"admin\"}]" >/dev/null
  echo "  Realm role 'admin' assigned to '$INITIAL_ADMIN_USERNAME' (IsSuperUser on first login)."
fi

echo "Done. User '$INITIAL_ADMIN_USERNAME' is ready — log in at the app to trigger JIT local provisioning."
