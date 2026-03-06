# ── Stage 1: Restore ─────────────────────────────────────────────────────────
# Copy only .csproj files first — Docker layer cache skips restore when only
# source files change, dramatically speeding up iterative builds.
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS restore
WORKDIR /src
COPY ["src/BoylikAI.Domain/BoylikAI.Domain.csproj",                "src/BoylikAI.Domain/"]
COPY ["src/BoylikAI.Application/BoylikAI.Application.csproj",      "src/BoylikAI.Application/"]
COPY ["src/BoylikAI.Infrastructure/BoylikAI.Infrastructure.csproj", "src/BoylikAI.Infrastructure/"]
COPY ["src/BoylikAI.API/BoylikAI.API.csproj",                       "src/BoylikAI.API/"]
RUN dotnet restore "src/BoylikAI.API/BoylikAI.API.csproj"

# ── Stage 2: Publish ──────────────────────────────────────────────────────────
FROM restore AS publish
COPY . .
RUN dotnet publish "src/BoylikAI.API/BoylikAI.API.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ── Stage 3: EF Migration Bundle ─────────────────────────────────────────────
# Bundles migrations into a self-contained binary — run it as an init container
# or via `docker run api ./efbundle` before starting the main container.
FROM publish AS efbundle
RUN dotnet tool install --global dotnet-ef --version 8.0.11
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
    --project src/BoylikAI.Infrastructure \
    --startup-project src/BoylikAI.API \
    --output /app/efbundle \
    --force

# ── Stage 4: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Non-root user — principle of least privilege
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

COPY --from=publish /app/publish .
COPY --from=efbundle /app/efbundle ./efbundle

# Only internal HTTP — TLS terminated by Nginx
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

USER appuser
ENTRYPOINT ["dotnet", "BoylikAI.API.dll"]
