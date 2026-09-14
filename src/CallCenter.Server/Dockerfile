# Build context is the repository root:
#   docker build -f src/CallCenter.Server/Dockerfile -t callcenter-api:latest .

# ---------- stage 1: build the React SPA ----------
FROM node:20-alpine AS web
WORKDIR /web

COPY src/CallCenter.Web/package.json src/CallCenter.Web/package-lock.json* ./
RUN npm ci || npm install

COPY src/CallCenter.Web/ ./
RUN npm run build


# ---------- stage 2: build and publish the API ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
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

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fsS http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "CallCenter.Server.dll"]
