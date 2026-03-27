# ── Stage 1: Restore ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS restore
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
# Debian slim — glibc bor, Whisper.net native library ishlaydi
# (Alpine/musl libc bilan Whisper.net mos kelmaydi)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# ffmpeg: OGG/Opus → WAV (Whisper uchun)
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Whisper model papkasi (Docker volume orqali mount qilinadi)
RUN mkdir -p /app/models

RUN groupadd -r appgroup && useradd -r -g appgroup appuser \
    && chown -R appuser:appgroup /app/models

COPY --from=publish /app/publish .

ENV DOTNET_ENVIRONMENT=Production

USER appuser
ENTRYPOINT ["dotnet", "BoylikAI.TelegramBot.dll"]
