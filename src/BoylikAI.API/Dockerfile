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

# ── Stage 3: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Non-root user — principle of least privilege
RUN addgroup -S appgroup && adduser -S appuser -G appgroup

COPY --from=publish /app/publish .

# Only internal HTTP — TLS terminated by Nginx
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

USER appuser
ENTRYPOINT ["dotnet", "BoylikAI.API.dll"]
