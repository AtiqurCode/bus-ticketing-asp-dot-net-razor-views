# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build — restore and publish against the .NET 10 SDK
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the heavy package pull caches until the project file changes.
COPY Busticketing.csproj ./
RUN dotnet restore Busticketing.csproj

# Then the rest of the source (respecting .dockerignore). Publish re-runs restore
# so the SDK's implicit Blazor asset package (blazor.web.js et al.) is resolved —
# a plain `--no-restore` publish drops it and the app loads with no interactivity.
COPY . .
RUN dotnet publish Busticketing.csproj -c Release -o /app /p:UseAppHost=false

# ---------------------------------------------------------------------------
# Runtime — ASP.NET Core 10 on Debian (ICU + tzdata already included, which
# the bn culture and the Asia/Dhaka clock both need)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# QuestPDF renders PDFs through SkiaSharp, which needs fontconfig on Linux.
# curl is here so the compose healthcheck has something to call.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Data-protection key ring lives here; mount a volume over it (see compose) so
# cookies/antiforgery tokens outlive the container.
RUN mkdir -p /app/keys && chown $APP_UID /app/keys

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Hosting__HttpsRedirection=false \
    DataProtection__KeyPath=/app/keys \
    DOTNET_gcServer=1

EXPOSE 8080
VOLUME ["/app/keys"]
USER $APP_UID

ENTRYPOINT ["dotnet", "BusTicketing.dll"]
