# Build context is the repository root:
#   docker build -f src/CallCenter.Server/Dockerfile -t callcenter-api:latest .

# ---------- stage 1: build the React SPA ----------
# Node 22: the web app's tools ask for 22.12 or later, and Node 20 has been
# end-of-life since April 2026 (M-S12, 27 Sep 2026).
FROM node:22-alpine AS web
WORKDIR /web

# npm ci alone: exactly the versions in the lockfile, the ones CI tested. The
# old "|| npm install" fell back to whatever the registry had that day.
COPY src/CallCenter.Web/package.json src/CallCenter.Web/package-lock.json ./
RUN npm ci

COPY src/CallCenter.Web/ ./
# The source maps stay out of the image: since 27 Sep they are no longer linked
# from the scripts (sourcemap: 'hidden'), but copied into wwwroot they could
# still be fetched by name, and they are the web app's source.
RUN npm run build && find dist -name '*.map' -delete


# ---------- stage 2: build and publish the API ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on the project files alone, so the layer caches.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/CallCenter.Shared/CallCenter.Shared.csproj src/CallCenter.Shared/
COPY src/CallCenter.Server/CallCenter.Server.csproj src/CallCenter.Server/
RUN dotnet restore src/CallCenter.Server/CallCenter.Server.csproj

COPY src/CallCenter.Shared/ src/CallCenter.Shared/
COPY src/CallCenter.Server/ src/CallCenter.Server/

# Drop the built SPA into wwwroot so it is published with the app.
COPY --from=web /web/dist/ src/CallCenter.Server/wwwroot/

RUN dotnet publish src/CallCenter.Server/CallCenter.Server.csproj \
      -c Release -o /app/publish --no-restore /p:UseAppHost=false


# ---------- stage 3: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:5000 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Asia/Hebron

# tzdata for Asia/Hebron; curl for the container healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

# Recordings are bind-mounted here by docker-compose.
RUN mkdir -p /data/recordings /app/logs

COPY --from=build /app/publish ./

# This version's deploy files, which auto-update.sh copies to the server
# before it installs the version. Never .env: that holds the site's secrets.
COPY deploy/docker-compose.yml deploy/update.sh deploy/backup.sh deploy/auto-update.sh /deploy/

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "CallCenter.Server.dll"]
