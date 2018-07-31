using System;
using System.Threading;
using System.Threading.Tasks;

namespace BigQuery.Linq
{
    public interface IQueryResponse<T>
    {
        T[] Rows { get; }
        int PageNumber { get; }
        ulong? TotalRows { get; }
        string PageToken { get; }
        bool HasNextPage { get; }
        long? TotalBytesProcessed { get; }
        string TotalBytesProcessedFormatted { get; }
        TimeSpan ExecutionTime { get; }
        T[] ToArray();
        Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default(CancellationToken));
    }
}