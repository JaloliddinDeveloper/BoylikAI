# BoylikAI — Production Architecture Document
*Principal Engineer Technical Design Document*

---

## 1. HIGH-LEVEL SYSTEM ARCHITECTURE

```
┌─────────────────────────────────────────────────────────────────────┐
│                          TELEGRAM CLIENT                            │
│  User sends: "Avtobusga 2400 so'm berdim"                          │
└──────────────────────┬──────────────────────────────────────────────┘
                       │ HTTPS / Webhook or Long Polling
                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      CLOUDFLARE / NGINX                             │
│              (TLS termination, DDoS protection, WAF)                │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
            ┌──────────┴──────────┐
            ▼                     ▼
┌─────────────────┐   ┌─────────────────────────────┐
│  BoylikAI.API   │   │  BoylikAI.TelegramBot Worker │
│  (.NET 8 Web)   │   │  (Long Polling / Webhook)    │
│                 │   │                              │
│  - REST API     │   │  - Message routing           │
│  - Webhook recv │   │  - Response formatting       │
│  - Hangfire UI  │   │  - Keyboard menus            │
└────────┬────────┘   └──────────────┬───────────────┘
         │                           │
         └──────────┬────────────────┘
                    ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    APPLICATION LAYER (MediatR CQRS)                 │
│                                                                     │
│  Commands:                    Queries:                              │
│  ├─ ParseAndCreateTransaction ├─ GetTransactions                   │
│  ├─ CreateTransaction         ├─ GetMonthlyReport                  │
│  ├─ RegisterUser              ├─ GetFinancialHealth                 │
│  └─ SetBudget                 ├─ GetSpendingPrediction              │
│                               └─ GetFinancialAdvice                 │
└──────────────────────┬──────────────────────────────────────────────┘
                       │
         ┌─────────────┼──────────────────┐
         ▼             ▼                  ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────────┐
│ PostgreSQL   │ │    Redis     │ │  Claude AI API   │
│ (Primary DB) │ │  (Cache +    │ │  (NLP Parsing +  │
│              │ │   Session)   │ │   Financial      │
│ - users      │ │              │ │   Advice)        │
│ - txns       │ │ - Analytics  │ │                  │
│ - budgets    │ │   cache 15m  │ │  claude-haiku-   │
│              │ │ - User cache │ │  4-5-20251001    │
└──────────────┘ └──────────────┘ └──────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    BACKGROUND JOBS (Hangfire)                       │
│                                                                     │
│  ┌─ DailyReportJob      (21:00 Tashkent time, daily)               │
│  ├─ BudgetCheckJob      (Every hour)                               │
│  └─ WeeklyInsightJob    (Every Sunday)                             │
└─────────────────────────────────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    OBSERVABILITY STACK                              │
│                                                                     │
│  Serilog → Seq          (Structured logs)                           │
│  OpenTelemetry → OTLP   (Traces)                                   │
│  Prometheus + Grafana   (Metrics & Dashboards)                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. CLEAN ARCHITECTURE PROJECT STRUCTURE

```
BoylikAI/
├── src/
│   ├── BoylikAI.Domain/              ← Core business rules, NO dependencies
│   │   ├── Common/
│   │   │   ├── Entity.cs             ← Base entity with domain events
│   │   │   └── IDomainEvent.cs
│   │   ├── Entities/
│   │   │   ├── User.cs               ← Aggregate root
│   │   │   ├── Transaction.cs        ← Core transaction entity
│   │   │   └── Budget.cs
│   │   ├── ValueObjects/
│   │   │   └── Money.cs              ← Immutable money with currency
│   │   ├── Events/                   ← Domain events (TransactionCreated, etc.)
│   │   └── Interfaces/               ← Repository contracts (no EF here)
│   │
│   ├── BoylikAI.Application/         ← Use cases, NO infrastructure dependencies
│   │   ├── Transactions/
│   │   │   ├── Commands/
│   │   │   │   ├── ParseAndCreate/   ← NLP → Transaction pipeline
│   │   │   │   └── CreateTransaction/
│   │   │   └── Queries/
│   │   ├── Analytics/Queries/        ← Reports, Health, Prediction, Advice
│   │   ├── Users/Commands/
│   │   ├── Common/
│   │   │   ├── Behaviors/            ← Logging + Validation MediatR pipelines
│   │   │   └── Interfaces/           ← ITransactionParser, IAnalyticsEngine, etc.
│   │   └── DTOs/                     ← Data transfer objects
│   │
│   ├── BoylikAI.Infrastructure/      ← External concerns implementation
│   │   ├── Persistence/
│   │   │   ├── ApplicationDbContext.cs
│   │   │   ├── Configurations/       ← EF fluent configurations
│   │   │   └── Repositories/         ← EF repository implementations
│   │   ├── AI/
│   │   │   ├── ClaudeTransactionParser.cs   ← NLP via Claude API
│   │   │   ├── RuleBasedCategoryClassifier.cs ← Fast keyword rules
│   │   │   └── ClaudeAdviceGenerator.cs     ← AI financial advice
│   │   ├── Analytics/
│   │   │   └── AnalyticsEngine.cs    ← Reports + predictions + health scores
│   │   ├── Caching/
│   │   │   └── RedisCacheService.cs
│   │   └── BackgroundJobs/
│   │       └── DailyReportJob.cs
│   │
│   ├── BoylikAI.API/                 ← HTTP API (thin controllers)
│   │   ├── Controllers/
│   │   │   ├── TransactionsController.cs
│   │   │   ├── AnalyticsController.cs
│   │   │   └── WebhookController.cs  ← Telegram webhook receiver
│   │   ├── Middleware/
│   │   │   └── ExceptionHandlingMiddleware.cs
│   │   └── Program.cs
│   │
│   └── BoylikAI.TelegramBot/         ← Telegram Worker Service
│       ├── Handlers/
│       │   └── MessageHandler.cs     ← NLP routing, command dispatch
│       ├── Services/
│       │   └── TelegramBotService.cs ← Message sending, notifications
│       ├── Keyboards/
│       │   └── InlineKeyboardBuilder.cs
│       └── Workers/
│           └── BotPollingWorker.cs   ← Dev: long polling
│
├── tests/
│   ├── BoylikAI.Domain.Tests/
│   └── BoylikAI.Application.Tests/
│
├── docker/
│   ├── postgres/init.sql
│   ├── prometheus/prometheus.yml
│   └── otel/otel-collector-config.yml
├── docker-compose.yml
└── .env.example
```

---

## 3. DATABASE SCHEMA DESIGN

### PostgreSQL Tables

```sql
-- ─────────────────────────────────────────────────
-- USERS TABLE
-- ─────────────────────────────────────────────────
CREATE TABLE users (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    telegram_id              BIGINT NOT NULL UNIQUE,
    username                 VARCHAR(128),
    first_name               VARCHAR(128),
    last_name                VARCHAR(128),
    language_code            VARCHAR(8) NOT NULL DEFAULT 'uz',
    default_currency         VARCHAR(8) NOT NULL DEFAULT 'UZS',
    monthly_budget_limit     NUMERIC(18,2),
    is_active                BOOLEAN NOT NULL DEFAULT TRUE,
    is_notifications_enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_activity_at         TIMESTAMPTZ
);
CREATE INDEX ix_users_telegram_id ON users (telegram_id);

-- ─────────────────────────────────────────────────
-- TRANSACTIONS TABLE
-- ─────────────────────────────────────────────────
CREATE TABLE transactions (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id             UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    type                SMALLINT NOT NULL,           -- 1=Expense, 2=Income, 3=Transfer
    amount              NUMERIC(18,2) NOT NULL,
    currency            VARCHAR(8) NOT NULL DEFAULT 'UZS',
    category            SMALLINT NOT NULL,           -- TransactionCategory enum
    description         VARCHAR(512) NOT NULL,
    original_message    VARCHAR(1024),               -- Raw user input
    transaction_date    DATE NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_ai_parsed        BOOLEAN NOT NULL DEFAULT FALSE,
    ai_confidence_score NUMERIC(4,3),               -- 0.000 to 1.000
    notes               VARCHAR(1024)
);

CREATE INDEX ix_transactions_user_id ON transactions (user_id);
CREATE INDEX ix_transactions_user_date ON transactions (user_id, transaction_date DESC);
CREATE INDEX ix_transactions_user_category ON transactions (user_id, category);
CREATE INDEX ix_transactions_user_month ON transactions (user_id, EXTRACT(YEAR FROM transaction_date), EXTRACT(MONTH FROM transaction_date));

-- ─────────────────────────────────────────────────
-- BUDGETS TABLE
-- ─────────────────────────────────────────────────
CREATE TABLE budgets (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    category     SMALLINT,                -- NULL = total budget
    limit_amount NUMERIC(18,2) NOT NULL,
    currency     VARCHAR(8) NOT NULL DEFAULT 'UZS',
    month        SMALLINT NOT NULL,
    year         SMALLINT NOT NULL,
    is_alert_sent BOOLEAN NOT NULL DEFAULT FALSE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX ix_budgets_user_period ON budgets (user_id, year, month);
```

### Key Design Decisions
- `transaction_date` stored as `DATE` not `TIMESTAMPTZ` — user-centric date (not server time)
- `original_message` stored for audit and retraining AI models
- `ai_confidence_score` enables filtering low-confidence transactions for manual review
- Partial indexes on `category` + `user_id` optimize monthly analytics queries
- `uuid-ossp` extension for server-side UUID generation

---

## 4. API ENDPOINT DESIGN

```
POST   /api/v1/users/{userId}/transactions          → Create manual transaction
GET    /api/v1/users/{userId}/transactions          → List with pagination+filters
GET    /api/v1/users/{userId}/transactions/{id}     → Single transaction
PUT    /api/v1/users/{userId}/transactions/{id}     → Update transaction
DELETE /api/v1/users/{userId}/transactions/{id}     → Soft delete

GET    /api/v1/users/{userId}/analytics/monthly     → Monthly report
GET    /api/v1/users/{userId}/analytics/weekly      → Weekly report
GET    /api/v1/users/{userId}/analytics/health      → Financial health score
GET    /api/v1/users/{userId}/analytics/prediction  → Month-end prediction
GET    /api/v1/users/{userId}/analytics/advice      → AI financial advice

POST   /api/webhook/telegram/{secretToken}          → Telegram webhook
GET    /health                                      → Health check
GET    /metrics                                     → Prometheus metrics
```

---

## 5. NLP TRANSACTION PARSER DESIGN

### Parsing Pipeline

```
Raw message
     │
     ▼
[1. Pre-filter]           Is it likely a financial message?
   Rule-based             Fast keyword check (no AI cost)
   keyword check          ~0.2ms
     │ YES
     ▼
[2. Claude Haiku LLM]     Semantic understanding
   claude-haiku-4-5        - Uzbek/Russian/mixed language
   ~500-800ms             - Handles informal writing
                          - Number normalization (ming/mln)
     │
     ▼
[3. Confidence check]     Score ≥ 0.65 → proceed
   threshold: 0.65        Score < 0.65 → ask clarification
     │ HIGH CONFIDENCE
     ▼
[4. Category correction]  Rule-based override for known keywords
   RuleBasedClassifier     when AI confidence 0.65-0.80
     │
     ▼
[5. Store + notify]       Transaction saved, confirmation sent
```

### Uzbek Number Normalization Examples
```
"2400 so'm"         → 2400 UZS
"35 ming"           → 35,000 UZS
"35000"             → 35,000 UZS
"5 million"         → 5,000,000 UZS
"2 mln"             → 2,000,000 UZS
"yarim million"     → 500,000 UZS
"1.5 million"       → 1,500,000 UZS
```

---

## 6. EXPENSE CATEGORIZATION ALGORITHM

### Two-Stage Classification

**Stage 1: Rule-Based (Fast, Deterministic)**
- Keyword dictionary per category (Uzbek + Russian + mixed)
- O(n×k) complexity, executes in <1ms
- Returns match score per category
- Used as: primary classifier and AI correction layer

**Stage 2: Claude AI (Semantic, Context-Aware)**
- Used when rule-based returns "Other" or low scores
- Understands context: "Uchrashuvdan qaytayotib non oldim" → Food (not Other)
- Handles misspellings and abbreviations
- Confidence score returned with every classification

**Hybrid Decision:**
```
if (claude_confidence >= 0.80):
    use claude_category
elif (rule_category != Other):
    use rule_category
else:
    use claude_category (best available)
```

---

## 7. SPENDING ANALYTICS ENGINE

### Monthly Report Calculation
```
For each TransactionCategory:
    amount_sum = SUM(transactions WHERE type=Expense AND category=X)
    percentage = amount_sum / total_expenses * 100

Net balance = total_income - total_expenses
```

### Financial Health Score
Uses a 100-point scoring model:
- Base score: 100
- Deductions: -15 per warning, -30 if balance negative
- Bonuses: +10 if savings_rate >= 20%
- Score mapping: Excellent(90+), Good(70+), Fair(50+), Poor(30+), Critical(<30)

### Recommended Spending Ratios (Adapted for Uzbekistan)
```
Food:          25% of income  (50/30/20 rule adapted)
Housing:       20% of income
Transport:     10% of income
Bills:         10% of income
Shopping:      10% of income
Health:         5% of income
Education:      5% of income
Entertainment:  5% of income
Savings:       20% of income  (mandatory target)
```

---

## 8. SPENDING PREDICTION ALGORITHM

### Weighted Moving Average Method
```
days_elapsed = current day of month
overall_daily_rate = current_spending / days_elapsed

recent_7d_spending = SUM(expenses in last 7 days)
moving_avg_daily = recent_7d_spending / min(7, days_elapsed)

weighted_daily_rate = (moving_avg * 0.6) + (overall_avg * 0.4)

predicted_total = current_spending + (weighted_daily_rate × remaining_days)
projected_savings = monthly_income - predicted_total
```

**Confidence Levels:**
- `High`: ≥ 7 days of data (statistically meaningful)
- `Medium`: 3-6 days
- `Low`: < 3 days

---

## 9. SCALABILITY STRATEGY (100K+ Users)

### Horizontal Scaling
```
Load Balancer (AWS ALB / Nginx)
    │
    ├── API Pod 1 (k8s)  ─┐
    ├── API Pod 2 (k8s)   ├── All stateless, scale freely
    └── API Pod N (k8s)  ─┘
                │
    ┌───────────┼───────────┐
    ▼           ▼           ▼
  Redis      Postgres    Bot Workers
  (shared)   Primary     (3 replicas)
               │
           Postgres
           Read Replica
           (analytics queries)
```

### Caching Strategy
| Data | Cache Key | TTL |
|------|-----------|-----|
| Monthly report | `analytics:{userId}:monthly:{year}:{month}` | 15 min |
| User lookup | `user:telegram:{telegramId}` | 1 hour |
| Financial advice | `advice:{userId}:{year}:{month}` | 30 min |
| Health score | `health:{userId}:{year}:{month}` | 15 min |

Cache invalidation: on every new transaction, `RemoveByPrefix("analytics:{userId}")`.

### Message Queue for Scale
For 100K+ users, Telegram webhook processing should be:
```
Telegram → API Webhook → RabbitMQ Queue → Bot Worker Consumers
                                         (3-10 workers, auto-scale)
```
This decouples receiving from processing, handles bursts, and allows retry.

### Rate Limiting
- Telegram webhook: 100 requests/second (Telegram sends max 1 update/user at a time)
- REST API: 300 requests/minute per IP
- AI parsing: internal circuit breaker, fallback to rule-based if Claude unavailable

---

## 10. SECURITY STRATEGY

### Telegram Bot Security
- Webhook URL contains a secret token (256-bit random): `/api/webhook/telegram/{secretToken}`
- Validates `secretToken` on every request
- IP allowlisting for Telegram's IP ranges (optional, via Nginx)

### Data Protection
- PostgreSQL password via environment variables (never in code)
- Anthropic API key via environment variable
- All secrets injected via `.env` or Kubernetes Secrets
- HTTPS enforced in production (TLS 1.3)
- No PII stored beyond what Telegram provides (telegram_id, username)
- Transaction `original_message` stored encrypted at rest (PostgreSQL encryption)

### API Security
- No unauthenticated external REST endpoints (all bot-facing via webhook)
- Admin endpoints protected by IP allowlist + basic auth
- SQL injection impossible via EF Core parameterized queries
- XSS not applicable (JSON API only)
- Rate limiting on all endpoints

---

## 11. PRODUCTION DEPLOYMENT (CLOUD)

### Kubernetes Architecture
```yaml
Namespace: boylikaI-prod
├── Deployment: boylikaI-api (3 replicas, HPA: 3-10)
├── Deployment: boylikaI-bot (2 replicas)
├── CronJob: daily-report (21:00 UTC+5)
├── Service: ClusterIP for api, bot
├── Ingress: nginx with TLS (cert-manager + Let's Encrypt)
├── ConfigMap: non-secret config
└── Secret: postgres-password, anthropic-key, bot-token

Managed Services:
├── AWS RDS PostgreSQL (Multi-AZ, db.t3.medium → db.r5.large at scale)
├── AWS ElastiCache Redis (cluster mode, 2 shards)
└── AWS ECR (container registry)
```

### CI/CD Pipeline (GitHub Actions)
```
Push to main
    │
    ├── Build & Test (dotnet test)
    ├── Docker Build & Push (ECR)
    ├── Helm upgrade --install (EKS)
    └── Smoke test (health check)
```

---

## 12. MONITORING & OBSERVABILITY

### Three Pillars

**Logs (Serilog → Seq)**
- Structured JSON logs with correlation IDs
- Request/response logging (path, status, duration)
- AI parsing success/failure rates
- Slow query detection (>500ms MediatR behavior)

**Metrics (Prometheus → Grafana)**
- API request rate, error rate, latency (P50/P95/P99)
- Cache hit rate
- Transaction creation rate
- Active users per hour
- AI API latency and error rate

**Traces (OpenTelemetry → Jaeger/Grafana Tempo)**
- Full request trace: Telegram → API → DB → AI → Response
- Slow span detection
- AI API call traces

### Key Dashboards
1. **Operations**: Request rate, error %, P95 latency, cache hit rate
2. **Business**: Daily active users, transactions/day, categories distribution
3. **AI**: Parse success rate, average confidence, fallback rate
4. **Infrastructure**: CPU, memory, DB connections, Redis memory

### Alerts
- Error rate > 1% → PagerDuty
- P95 latency > 2s → Slack
- DB connections > 80% → Email
- AI API failure > 5 consecutive → Circuit breaker + fallback to rules
