using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Infrastructure.Export;

public sealed class ExcelExportService : IExcelExportService
{
    private readonly ITransactionRepository _txRepo;
    private readonly ILogger<ExcelExportService> _logger;

    // Ranglar
    private static readonly XLColor HeaderBg    = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor HeaderFg    = XLColor.White;
    private static readonly XLColor IncomeBg    = XLColor.FromHtml("#E8F5E9");
    private static readonly XLColor ExpenseBg   = XLColor.FromHtml("#FFEBEE");
    private static readonly XLColor SummaryBg   = XLColor.FromHtml("#E3F2FD");
    private static readonly XLColor SubHeaderBg = XLColor.FromHtml("#2E75B6");
    private static readonly XLColor AltRowBg    = XLColor.FromHtml("#F5F5F5");

    public ExcelExportService(ITransactionRepository txRepo, ILogger<ExcelExportService> logger)
    {
        _txRepo = txRepo;
        _logger = logger;
    }

    public async Task<(byte[] Bytes, string FileName)> ExportAsync(
        Guid userId, ExportPeriod period, CancellationToken ct = default)
    {
        var (from, to, periodLabel) = GetDateRange(period);
        var transactions = await _txRepo.GetByUserIdAndDateRangeAsync(userId, from, to, ct);

        using var wb = new XLWorkbook();
        wb.Properties.Author = "BoylikAI";
        wb.Properties.Title  = $"Moliyaviy hisobot — {periodLabel}";

        BuildSummarySheet(wb, transactions, periodLabel, from, to);
        BuildTransactionsSheet(wb, transactions);
        BuildCategorySheet(wb, transactions);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var fileName = $"BoylikAI_{period}_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";
        _logger.LogInformation("Excel exported for user {UserId}: {Period}, {Count} transactions",
            userId, period, transactions.Count);

        return (ms.ToArray(), fileName);
    }

    // ── Sheet 1: Umumiy ko'rinish ─────────────────────────────────────────────
    private static void BuildSummarySheet(
        IXLWorkbook wb,
        IReadOnlyList<Transaction> txs,
        string periodLabel,
        DateOnly from, DateOnly to)
    {
        var ws = wb.Worksheets.Add("📊 Umumiy");
        ws.ShowGridLines = false;

        // Sarlavha
        var title = ws.Range("A1:F1");
        title.Merge();
        title.Value = $"BoylikAI — Moliyaviy Hisobot";
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = 18;
        title.Style.Font.FontColor = HeaderBg;
        title.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var subtitle = ws.Range("A2:F2");
        subtitle.Merge();
        subtitle.Value = $"{periodLabel}  ({from:dd.MM.yyyy} – {to:dd.MM.yyyy})";
        subtitle.Style.Font.FontSize = 11;
        subtitle.Style.Font.FontColor = XLColor.Gray;
        subtitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Row(3).Height = 8;

        // Jami kartalar
        var income   = txs.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount.Amount);
        var expenses = txs.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount.Amount);
        var balance  = income - expenses;
        var currency = txs.FirstOrDefault()?.Amount.Currency ?? "UZS";

        AddSummaryCard(ws, "A4", "💰 Jami Daromad",   income,   currency, IncomeBg,  XLColor.FromHtml("#2E7D32"));
        AddSummaryCard(ws, "C4", "💸 Jami Xarajat",   expenses, currency, ExpenseBg, XLColor.FromHtml("#C62828"));
        AddSummaryCard(ws, "E4", "📈 Balans",          balance,  currency,
            balance >= 0 ? IncomeBg : ExpenseBg,
            balance >= 0 ? XLColor.FromHtml("#2E7D32") : XLColor.FromHtml("#C62828"));

        ws.Row(7).Height = 8;

        // Kategoriya jadvali
        SetHeader(ws, "A8:F8", "Kategoriya bo'yicha tahlil");
        var catHeaders = new[] { "Kategoriya", "Tur", "Tranzaksiyalar", "Jami (UZS)", "Ulush (%)", "O'rtacha" };
        WriteTableHeader(ws, 9, "A", catHeaders, HeaderBg);

        var categories = txs
            .GroupBy(t => (t.Category, t.Type))
            .Select(g => new
            {
                Category = g.Key.Category,
                Type     = g.Key.Type,
                Count    = g.Count(),
                Total    = g.Sum(t => t.Amount.Amount),
                Avg      = g.Average(t => t.Amount.Amount)
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        var totalAll = categories.Sum(c => c.Total);
        var row = 10;
        foreach (var (cat, i) in categories.Select((c, i) => (c, i)))
        {
            var bg = i % 2 == 0 ? XLColor.White : AltRowBg;
            ws.Cell(row, 1).Value = cat.Category.ToString();
            ws.Cell(row, 2).Value = cat.Type == TransactionType.Income ? "Daromad" : "Xarajat";
            ws.Cell(row, 3).Value = cat.Count;
            ws.Cell(row, 4).Value = cat.Total;
            ws.Cell(row, 5).Value = totalAll > 0 ? Math.Round(cat.Total / totalAll * 100, 1) : 0;
            ws.Cell(row, 6).Value = Math.Round(cat.Avg, 0);

            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 5).Style.NumberFormat.Format = "0.0\"%\"";
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";

            var rowBg = cat.Type == TransactionType.Income ? IncomeBg : ExpenseBg;
            ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = rowBg;
            ws.Range(row, 1, row, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            row++;
        }

        // Ustun kengliklari
        ws.Column(1).Width = 22;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 16;
        ws.Column(4).Width = 18;
        ws.Column(5).Width = 12;
        ws.Column(6).Width = 18;
    }

    // ── Sheet 2: Barcha tranzaksiyalar ────────────────────────────────────────
    private static void BuildTransactionsSheet(IXLWorkbook wb, IReadOnlyList<Transaction> txs)
    {
        var ws = wb.Worksheets.Add("📋 Tranzaksiyalar");
        ws.ShowGridLines = false;

        SetHeader(ws, "A1:G1", "Barcha Tranzaksiyalar");

        var headers = new[] { "Sana", "Tur", "Miqdor (UZS)", "Valyuta", "Kategoriya", "Tavsif", "AI Parsed" };
        WriteTableHeader(ws, 2, "A", headers, HeaderBg);

        var sorted = txs.OrderByDescending(t => t.TransactionDate).ThenByDescending(t => t.CreatedAt).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var t   = sorted[i];
            var row = i + 3;
            var bg  = t.Type == TransactionType.Income ? IncomeBg : ExpenseBg;

            ws.Cell(row, 1).Value = t.TransactionDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.NumberFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 2).Value = t.Type == TransactionType.Income ? "💰 Daromad" : "💸 Xarajat";
            ws.Cell(row, 3).Value = t.Amount.Amount;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 4).Value = t.Amount.Currency;
            ws.Cell(row, 5).Value = t.Category.ToString();
            ws.Cell(row, 6).Value = t.Description;
            ws.Cell(row, 7).Value = t.IsAiParsed ? "✓" : "";

            ws.Range(row, 1, row, 7).Style.Fill.BackgroundColor = bg;
            ws.Range(row, 1, row, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 7).Style.Border.InsideBorder  = XLBorderStyleValues.Hair;
        }

        // Jami qator
        if (sorted.Count > 0)
        {
            var totalRow = sorted.Count + 3;
            ws.Range(totalRow, 1, totalRow, 2).Merge().Value = "JAMI";
            ws.Range(totalRow, 1, totalRow, 2).Style.Font.Bold = true;
            ws.Cell(totalRow, 3).FormulaA1 = $"=SUM(C3:C{totalRow - 1})";
            ws.Cell(totalRow, 3).Style.NumberFormat.Format = "#,##0";
            ws.Range(totalRow, 1, totalRow, 7).Style.Fill.BackgroundColor = SummaryBg;
            ws.Range(totalRow, 1, totalRow, 7).Style.Font.Bold = true;
        }

        ws.Column(1).Width = 14;
        ws.Column(2).Width = 14;
        ws.Column(3).Width = 18;
        ws.Column(4).Width = 10;
        ws.Column(5).Width = 18;
        ws.Column(6).Width = 35;
        ws.Column(7).Width = 12;

        // Auto-filter
        if (sorted.Count > 0)
            ws.RangeUsed()?.SetAutoFilter();
    }

    // ── Sheet 3: Kunlik tahlil ────────────────────────────────────────────────
    private static void BuildCategorySheet(IXLWorkbook wb, IReadOnlyList<Transaction> txs)
    {
        var ws = wb.Worksheets.Add("📅 Kunlik");
        ws.ShowGridLines = false;

        SetHeader(ws, "A1:F1", "Kunlik Xarajat va Daromad");

        var headers = new[] { "Sana", "Daromad (UZS)", "Xarajat (UZS)", "Balans (UZS)", "Tranzaksiyalar", "Eng katta xarajat" };
        WriteTableHeader(ws, 2, "A", headers, SubHeaderBg);

        var dailyGroups = txs
            .GroupBy(t => t.TransactionDate)
            .OrderByDescending(g => g.Key)
            .ToList();

        for (int i = 0; i < dailyGroups.Count; i++)
        {
            var grp     = dailyGroups[i];
            var row     = i + 3;
            var inc     = grp.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount.Amount);
            var exp     = grp.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount.Amount);
            var balance = inc - exp;
            var bigExp  = grp.Where(t => t.Type == TransactionType.Expense)
                             .OrderByDescending(t => t.Amount.Amount)
                             .FirstOrDefault();

            ws.Cell(row, 1).Value = grp.Key.ToDateTime(TimeOnly.MinValue);
            ws.Cell(row, 1).Style.NumberFormat.Format = "dd.MM.yyyy (ddd)";
            ws.Cell(row, 2).Value = inc;
            ws.Cell(row, 3).Value = exp;
            ws.Cell(row, 4).Value = balance;
            ws.Cell(row, 5).Value = grp.Count();
            ws.Cell(row, 6).Value = bigExp is not null
                ? $"{bigExp.Description} ({bigExp.Amount.Amount:N0} {bigExp.Amount.Currency})"
                : "—";

            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";

            ws.Cell(row, 4).Style.Font.FontColor = balance >= 0
                ? XLColor.FromHtml("#2E7D32")
                : XLColor.FromHtml("#C62828");

            var bg = i % 2 == 0 ? XLColor.White : AltRowBg;
            ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = bg;
            ws.Range(row, 1, row, 6).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 6).Style.Border.InsideBorder  = XLBorderStyleValues.Hair;
        }

        ws.Column(1).Width = 20;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 18;
        ws.Column(4).Width = 18;
        ws.Column(5).Width = 16;
        ws.Column(6).Width = 40;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddSummaryCard(
        IXLWorksheet ws, string cell, string label, decimal value, string currency, XLColor bg, XLColor fg)
    {
        var startCol = ws.Cell(cell).Address.ColumnNumber;
        var startRow = ws.Cell(cell).Address.RowNumber;
        var r = ws.Range(startRow, startCol, startRow + 2, startCol + 1);
        r.Merge();
        r.Style.Fill.BackgroundColor = bg;
        r.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        r.Style.Border.OutsideBorderColor = fg;
        r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        r.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        r.Style.Alignment.WrapText   = true;

        r.Value = $"{label}\n{value:N0} {currency}";
        r.Style.Font.Bold      = true;
        r.Style.Font.FontSize  = 12;
        r.Style.Font.FontColor = fg;
        ws.Row(startRow).Height     = 18;
        ws.Row(startRow + 1).Height = 18;
        ws.Row(startRow + 2).Height = 18;
        ws.Column(startCol).Width = 22;
    }

    private static void SetHeader(IXLWorksheet ws, string range, string text)
    {
        var r = ws.Range(range);
        r.Merge();
        r.Value = text;
        r.Style.Font.Bold = true;
        r.Style.Font.FontSize = 13;
        r.Style.Font.FontColor = HeaderBg;
        r.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Row(r.FirstRow().RowNumber()).Height = 20;
    }

    private static void WriteTableHeader(IXLWorksheet ws, int row, string startCol, string[] headers, XLColor bg)
    {
        var col = ws.Cell(row, startCol).Address.ColumnNumber;
        foreach (var header in headers)
        {
            var cell = ws.Cell(row, col++);
            cell.Value = header;
            cell.Style.Fill.BackgroundColor   = bg;
            cell.Style.Font.Bold              = true;
            cell.Style.Font.FontColor         = XLColor.White;
            cell.Style.Alignment.Horizontal   = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder   = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.White;
        }
        ws.Row(row).Height = 22;
    }

    private static (DateOnly From, DateOnly To, string Label) GetDateRange(ExportPeriod period)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return period switch
        {
            ExportPeriod.Daily   => (today, today, $"Bugun — {today:dd MMMM yyyy}"),
            ExportPeriod.Weekly  => (today.AddDays(-6), today, $"So'nggi 7 kun ({today.AddDays(-6):dd.MM} – {today:dd.MM.yyyy})"),
            ExportPeriod.Monthly => (
                new DateOnly(today.Year, today.Month, 1),
                today,
                $"{today:MMMM yyyy}"),
            _ => (today, today, "Bugun")
        };
    }
}
