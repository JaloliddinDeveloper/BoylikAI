namespace BoylikAI.Application.Common.Interfaces;

public enum ExportPeriod { Daily, Weekly, Monthly }

public interface IExcelExportService
{
    Task<(byte[] Bytes, string FileName)> ExportAsync(
        Guid userId,
        ExportPeriod period,
        CancellationToken ct = default);
}
