using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using MediatR;

namespace BoylikAI.Application.Analytics.Queries.GetMonthlyReport;

public sealed class GetMonthlyReportQueryHandler : IRequestHandler<GetMonthlyReportQuery, AnalyticsReportDto>
{
    private readonly IAnalyticsEngine _analyticsEngine;
    private readonly ICacheService _cache;

    public GetMonthlyReportQueryHandler(IAnalyticsEngine analyticsEngine, ICacheService cache)
    {
        _analyticsEngine = analyticsEngine;
        _cache = cache;
    }

    public async Task<AnalyticsReportDto> Handle(
        GetMonthlyReportQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"analytics:{request.UserId}:monthly:{request.Year}:{request.Month}";

        return await _cache.GetOrSetAsync(
            cacheKey,
            async ct => await _analyticsEngine.GetMonthlyReportAsync(request.UserId, request.Year, request.Month, ct),
            TimeSpan.FromMinutes(15),
            cancellationToken);
    }
}

public sealed class GetSpendingPredictionQueryHandler : IRequestHandler<GetSpendingPredictionQuery, SpendingPredictionDto>
{
    private readonly IAnalyticsEngine _analyticsEngine;

    public GetSpendingPredictionQueryHandler(IAnalyticsEngine analyticsEngine)
    {
        _analyticsEngine = analyticsEngine;
    }

    public Task<SpendingPredictionDto> Handle(GetSpendingPredictionQuery request, CancellationToken cancellationToken) =>
        _analyticsEngine.PredictMonthEndSpendingAsync(request.UserId, request.Year, request.Month, cancellationToken);
}

public sealed class GetFinancialHealthQueryHandler : IRequestHandler<GetFinancialHealthQuery, FinancialHealthDto>
{
    private readonly IAnalyticsEngine _analyticsEngine;

    public GetFinancialHealthQueryHandler(IAnalyticsEngine analyticsEngine)
    {
        _analyticsEngine = analyticsEngine;
    }

    public Task<FinancialHealthDto> Handle(GetFinancialHealthQuery request, CancellationToken cancellationToken) =>
        _analyticsEngine.GetFinancialHealthAsync(request.UserId, request.Year, request.Month, cancellationToken);
}

public sealed class GetFinancialAdviceQueryHandler : IRequestHandler<GetFinancialAdviceQuery, FinancialAdviceDto>
{
    private readonly IAnalyticsEngine _analyticsEngine;
    private readonly IAdviceGenerator _adviceGenerator;

    public GetFinancialAdviceQueryHandler(IAnalyticsEngine analyticsEngine, IAdviceGenerator adviceGenerator)
    {
        _analyticsEngine = analyticsEngine;
        _adviceGenerator = adviceGenerator;
    }

    public async Task<FinancialAdviceDto> Handle(GetFinancialAdviceQuery request, CancellationToken cancellationToken)
    {
        var health = await _analyticsEngine.GetFinancialHealthAsync(
            request.UserId, request.Year, request.Month, cancellationToken);

        return await _adviceGenerator.GenerateAdviceAsync(
            request.UserId, health, request.LanguageCode, cancellationToken);
    }
}
