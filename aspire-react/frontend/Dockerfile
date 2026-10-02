# syntax=docker/dockerfile:1
# Mirats / AspireReact — Frontend (Vite + React 19)
# Build context: aspire-react/frontend
#   docker build -f Dockerfile -t aspirereact-frontend:latest .
#
# VITE_* are build-time (baked into the bundle). Pass them as --build-arg so the
# image is reproducible per environment; compose sets them from .env.
#   docker build --build-arg VITE_API_BASE_URL=/api/v1 .
# [FIX-DEPLOY 2026-10-02] The three VITE_KEYCLOAK_* build args were removed together with Keycloak
# (AUTH Phase 5 → local JWT auth; nothing in src/ reads them any more).

FROM node:20-alpine AS build
WORKDIR /app

# Build args — optional (has a sane default in vite.config.ts / api-client.ts).
# Declared as ARG so `docker build --build-arg` can override; also exposed as ENV
# so Vite's `import.meta.env.*` picks them up at build time.
ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ── Runtime: Nginx serves the static bundle and proxies /api → backend ──
FROM nginx:1.27-alpine AS runtime

# Nginx config: SPA fallback (try_files → index.html) + /api reverse proxy to `server:5000`.
# `server` is the compose service name for the backend; at runtime Nginx resolves it via
# Docker's embedded DNS. No hard-coded host IP.
COPY nginx.conf /etc/nginx/conf.d/default.conf

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:80/ || exit 1
