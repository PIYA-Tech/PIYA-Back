using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed partial class DatabasePerformanceInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<(string Type, string Table), Aggregate> _metrics = new();

    public List<DatabaseQueryMetric> Snapshot(int top) => _metrics
        .Select(item => new DatabaseQueryMetric
        {
            QueryType = item.Key.Type,
            TableName = item.Key.Table,
            ExecutionCount = item.Value.Count,
            TotalDurationMs = (long)Math.Round(item.Value.TotalDurationMs),
            AverageDurationMs = item.Value.Count == 0 ? 0 : item.Value.TotalDurationMs / (double)item.Value.Count,
            CacheHitRate = 0,
        })
        .OrderByDescending(item => item.TotalDurationMs)
        .Take(Math.Clamp(top, 1, 100))
        .ToList();

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command.CommandText, eventData.Duration);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command.CommandText, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command.CommandText, eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Record(command.CommandText, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command.CommandText, eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command.CommandText, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    private void Record(string sql, TimeSpan duration)
    {
        var operationMatch = SqlOperationRegex().Match(sql);
        var tableMatch = SqlTableRegex().Match(sql);
        var type = operationMatch.Success ? operationMatch.Groups[1].Value.ToUpperInvariant() : "OTHER";
        var table = tableMatch.Success ? tableMatch.Groups[1].Value.Trim('"') : "unknown";
        _metrics.AddOrUpdate(
            (type, table),
            _ => new Aggregate(1, duration.TotalMilliseconds),
            (_, current) => new Aggregate(current.Count + 1, current.TotalDurationMs + duration.TotalMilliseconds));
    }

    [GeneratedRegex("(?is)^\\s*(SELECT|INSERT|UPDATE|DELETE)")]
    private static partial Regex SqlOperationRegex();

    [GeneratedRegex("(?is)(?:FROM|INTO|UPDATE)\\s+([\\w\\\".]+)")]
    private static partial Regex SqlTableRegex();

    private readonly record struct Aggregate(long Count, double TotalDurationMs);
}
