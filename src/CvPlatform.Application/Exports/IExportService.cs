using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Exports;

public interface IExportService
{
    Task<Result<byte[]>> ExportCvPdfAsync(
        ActorContext actor, Guid cvId, string publicBaseUrl, CancellationToken ct = default);
    Task<Result<byte[]>> ExportPositionsExcelAsync(
        ActorContext actor, CancellationToken ct = default);
    Task<Result<byte[]>> ExportPositionCvsExcelAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);
    Task<Result<byte[]>> ExportPositionCvsCsvAsync(
        ActorContext actor, Guid positionId, CancellationToken ct = default);
}
