# ── Stage 1: Restore ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS restore
WORKDIR /src
COPY ["src/BoylikAI.Domain/BoylikAI.Domain.csproj",                "src/BoylikAI.Domain/"]
COPY ["src/BoylikAI.Application/BoylikAI.Application.csproj",      "src/BoylikAI.Application/"]
COPY ["src/BoylikAI.Infrastructure/BoylikAI.Infrastructure.csproj", "src/BoylikAI.Infrastructure/"]
COPY ["src/BoylikAI.TelegramBot/BoylikAI.TelegramBot.csproj",      "src/BoylikAI.TelegramBot/"]
RUN dotnet restore "src/BoylikAI.TelegramBot/BoylikAI.TelegramBot.csproj"

# ── Stage 2: Publish ──────────────────────────────────────────────────────────
FROM restore AS publish
COPY . .
RUN dotnet publish "src/BoylikAI.TelegramBot/BoylikAI.TelegramBot.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ── Stage 3: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# ffmpeg — Telegram OGG/Opus → WAV (Whisper uchun)
RUN apk add --no-cache ffmpeg

# Whisper model papkasi (volume orqali mount qilinadi)
RUN mkdir -p /app/models

RUN addgroup -S appgroup && adduser -S appuser -G appgroup \
    && chown -R appuser:appgroup /app/models

COPY --from=publish /app/publish .

ENV DOTNET_ENVIRONMENT=Production

USER appuser
ENTRYPOINT ["dotnet", "BoylikAI.TelegramBot.dll"]
