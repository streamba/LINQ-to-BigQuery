﻿using Google.Apis.Bigquery.v2.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BigQuery.Linq.Extensions;
using Google.Apis.Bigquery.v2;

namespace BigQuery.Linq
{
    public class QueryResponse<T> : IQueryResponse<T>
    {
        public string Query { get; }
        public bool? CacheHit { get; }
        public string ETag { get; }
        public string Kind { get; }
        public string PageToken { get; }

        /// <summary>Returns current page rows. If you needs all paged rows, use ToArray or ToArrayAsync instead.</summary>
        public T[] Rows { get; }

        public long? TotalBytesProcessed { get; }
        public string TotalBytesProcessedFormatted { get; }
        public ulong? TotalRows { get; }
        public TimeSpan ExecutionTime { get; }
        public IList<TableFieldSchema> TableFieldSchemas { get; }
        public int PageNumber { get; private set; }

        // there's only another page if the previous response contained a PageToken 
        // (see https://cloud.google.com/bigquery/docs/data#paging)
        public bool HasNextPage => PageToken != null;

        private readonly bool _jobComplete;
        private readonly JobReference _jobReference;
        private readonly BigQueryContext _context;
        private readonly IRowsParser _rowsParser;
        private readonly bool _isDynamic;

        internal QueryResponse(BigQueryContext context, string query, TimeSpan executionTime,
            QueryResponse queryResponse, bool isDynamic, IRowsParser rowsParser, int pageNumber = 1)
            : this(
                context, query, executionTime, isDynamic, rowsParser, queryResponse.Rows, queryResponse.Schema,
                queryResponse.CacheHit, queryResponse.ETag, queryResponse.Kind,
                queryResponse.PageToken, queryResponse.TotalBytesProcessed, queryResponse.TotalRows,
                queryResponse.JobComplete, queryResponse.JobReference, pageNumber)
        {
        }

        private QueryResponse(BigQueryContext context, string query, TimeSpan executionTime,
            GetQueryResultsResponse queryResponse, bool isDynamic, IRowsParser rowsParser, int pageNumber)
            : this(
                context, query, executionTime, isDynamic, rowsParser, queryResponse.Rows, queryResponse.Schema,
                queryResponse.CacheHit, queryResponse.ETag, queryResponse.Kind,
                queryResponse.PageToken, queryResponse.TotalBytesProcessed, queryResponse.TotalRows,
                queryResponse.JobComplete, queryResponse.JobReference, pageNumber)
        {
        }

        private QueryResponse(BigQueryContext context, string query, TimeSpan executionTime, bool isDynamic,
            IRowsParser rowsParser, IList<TableRow> rows, TableSchema schema, bool? cacheHit, string eTag, string kind,
            string pageToken, long? totalBytesProcessed, ulong? totalRows,
            bool? jobComplete, JobReference jobReference, int pageNumber)
        {
            _context = context;
            _isDynamic = isDynamic;
            _rowsParser = rowsParser;

            Rows = rows == null
                ? new T[0]
                : rowsParser.Parse<T>(
                    context.fallbacks,
                    context.IsConvertResultUtcToLocalTime,
                    schema,
                    rows,
                    isDynamic).ToArray();

            TableFieldSchemas = (schema == null)
                ? new TableFieldSchema[0]
                : schema.Fields;

            Query = query;
            ExecutionTime = executionTime;
            CacheHit = cacheHit;
            ETag = eTag;
            Kind = kind;
            PageToken = pageToken;
            TotalBytesProcessed = totalBytesProcessed;
            TotalRows = totalRows;
            PageNumber = pageNumber;

            TotalBytesProcessedFormatted = totalBytesProcessed.ToHumanReadableSize();

            _jobComplete = jobComplete.GetValueOrDefault(false);
            _jobReference = jobReference;
        }

        public IQueryResponse<T> GetNextResponse()
        {
            var request = CreateNextPageRequest(_context, _jobComplete, _jobReference, PageToken, HasNextPage);

            var sw = Stopwatch.StartNew();
            var furtherQueryResponse = request.Execute();
            sw.Stop();

            return new QueryResponse<T>(_context, Query, sw.Elapsed, furtherQueryResponse, _isDynamic, _rowsParser,
                PageNumber + 1);
        }

        public async Task<IQueryResponse<T>> GetNextResponseAsync(CancellationToken token = default(CancellationToken))
        {
            var request = CreateNextPageRequest(_context, _jobComplete, _jobReference, PageToken, HasNextPage);

            var sw = Stopwatch.StartNew();
            var furtherQueryResponse = await new Func<Task<GetQueryResultsResponse>>(() => request.ExecuteAsync(token))
                .TryWithRetryCountAsync();
            sw.Stop();

            return new QueryResponse<T>(_context, Query, sw.Elapsed, furtherQueryResponse, _isDynamic, _rowsParser,
                PageNumber + 1);
        }

        private JobsResource.GetQueryResultsRequest CreateNextPageRequest(BigQueryContext context,
            bool jobComplete, JobReference jobReference, string pageToken, bool hasNextPage)
        {
            if (!jobComplete)
            {
                throw new InvalidOperationException("Can't get next page for a job that has not completed");
            }

            if (!hasNextPage)
            {
                throw new InvalidOperationException("No more pages to retrieve");
            }

            var furtherRequest =
                context.BigQueryService.Jobs.GetQueryResults(jobReference.ProjectId, jobReference.JobId);
            furtherRequest.PageToken = pageToken;
            return furtherRequest;
        }

        /// <summary>
        /// Get paging result.
        /// </summary>
        public T[] ToArray()
        {
            var result = this;
            var rows = new List<T>((int) result.TotalRows.GetValueOrDefault((ulong) result.Rows.Length));

            rows.AddRange(result.Rows);

            while (result.HasNextPage)
            {
                result = (QueryResponse<T>) result.GetNextResponse();
                rows.AddRange(result.Rows);
            }

            return rows.ToArray();
        }


        /// <summary>
        /// Get paging result.
        /// </summary>
        public async Task<T[]> ToArrayAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = this;
            var rows = new List<T>((int) result.TotalRows.GetValueOrDefault((ulong) result.Rows.Length));

            rows.AddRange(result.Rows);

            while (result.HasNextPage)
            {
                result = (QueryResponse<T>) await result.GetNextResponseAsync(cancellationToken).ConfigureAwait(false);
                rows.AddRange(result.Rows);
            }

            return rows.ToArray();
        }
    }
}