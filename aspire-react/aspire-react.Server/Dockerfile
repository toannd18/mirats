# syntax=docker/dockerfile:1
# Mirats / AspireReact — Backend API
# Build context: aspire-react/  (so `docker build -f aspire-react.Server/Dockerfile -t aspirereact-server aspire-react/`
#   also matches `docker-compose.yml` with `context: ./aspire-react` + `dockerfile: aspire-react.Server/Dockerfile`)
# This keeps the solution layout described in AGENTS.md (sln lives under aspire-react/).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY ["aspire-react.Server/aspire-react.Server.csproj", "aspire-react.Server/"]
COPY ["aspire-react.ServiceDefaults/aspire-react.ServiceDefaults.csproj", "aspire-react.ServiceDefaults/"]
RUN dotnet restore "aspire-react.Server/aspire-react.Server.csproj"
COPY aspire-react.Server/ aspire-react.Server/
COPY aspire-react.ServiceDefaults/ aspire-react.ServiceDefaults/
# Clear any stale obj/ that might have been copied before .dockerignore took effect
# (Windows obj/project.assets.json contains fallback path C:\... which breaks Linux publish).
RUN rm -rf aspire-react.Server/obj aspire-react.Server/bin \
           aspire-react.ServiceDefaults/obj aspire-react.ServiceDefaults/bin
WORKDIR /src/aspire-react.Server
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Kestrel listen — prod default :5000 (single source of truth).
# Compose may override via ASPNETCORE_URLS env; do NOT hard-code a different port here.
# launchSettings.json dev ports 5428/7314 are dev-only and not used in this image.
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

# Healthcheck: TCP-connect to :5000 (same as docker-compose.yml). Do NOT use HTTP
# /health: (1) aspnet:10.0 has no wget/curl; (2) ServiceDefaults /health only exists in
# Development, and prod runs Production where it returns 404. bash /dev/tcp works everywhere.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/5000' || exit 1

ENTRYPOINT ["dotnet", "aspire-react.Server.dll"]
