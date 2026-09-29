using System;
using Microsoft.Extensions.Logging;

namespace RestaurantPos.Infrastructure.Printing;

internal static partial class PrintLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Impression échouée sur {PrinterName} (job {JobId}, essai {Attempt}).")]
    public static partial void SendFailed(ILogger logger, Exception exception, string printerName, Guid jobId, int attempt);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Aucune imprimante pour {Target} : rien mis en file.")]
    public static partial void NoPrinter(ILogger logger, string target);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "Mise en file d'impression impossible.")]
    public static partial void QueueFailed(ILogger logger, Exception exception);
}
