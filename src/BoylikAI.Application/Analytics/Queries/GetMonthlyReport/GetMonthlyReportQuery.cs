using BoylikAI.Application.DTOs;
using MediatR;

namespace BoylikAI.Application.Analytics.Queries.GetMonthlyReport;

public sealed record GetMonthlyReportQuery(
    Guid UserId,
    int Year,
    int Month) : IRequest<AnalyticsReportDto>;

public sealed record GetFinancialAdviceQuery(
    Guid UserId,
    int Year,
    int Month,
    string LanguageCode = "uz") : IRequest<FinancialAdviceDto>;

public sealed record GetSpendingPredictionQuery(
    Guid UserId,
    int Year,
    int Month) : IRequest<SpendingPredictionDto>;

public sealed record GetFinancialHealthQuery(
    Guid UserId,
    int Year,
    int Month) : IRequest<FinancialHealthDto>;
