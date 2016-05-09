﻿using Google.Apis.Bigquery.v2.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Bigquery.v2;

namespace BigQuery.Linq
{
    public class QueryResponse<T>
    {
        public string Query { get; }
        public bool? CacheHit { get; }
        public string ETag { get; }
        public string Kind { get; }
        public string PageToken { get; }
        public T[] Rows { get; }
        public long? TotalBytesProcessed { get; }
        public string TotalBytesProcessedFormatted { get; }
        public ulong? TotalRows { get; }
        public TimeSpan ExecutionTime { get; }
        public IList<TableFieldSchema> TableFieldSchemas { get; }

        // there's only another page if the previous response contained a PageToken 
        // (see https://cloud.google.com/bigquery/docs/data#paging)
        public bool HasNextPage => PageToken != null;
        
        private readonly bool _jobComplete;
        private readonly JobReference _jobReference;
        private readonly BigQueryContext _context;
        private readonly IRowsParser _rowsParser;
        private readonly bool _isDynamic;
        private const int RetryCount = 50;

        internal QueryResponse(BigQueryContext context, string query, TimeSpan executionTime,
            QueryResponse queryResponse, bool isDynamic, IRowsParser rowsParser)
            : this(
                context, query, executionTime, isDynamic, rowsParser, queryResponse.Rows, queryResponse.Schema,
                queryResponse.CacheHit, queryResponse.ETag, queryResponse.Kind,
                queryResponse.PageToken, queryResponse.TotalBytesProcessed, queryResponse.TotalRows,
                queryResponse.JobComplete, queryResponse.JobReference)
        {
        }

        private QueryResponse(BigQueryContext context, string query, TimeSpan executionTime,
            GetQueryResultsResponse queryResponse, bool isDynamic, IRowsParser rowsParser)
            : this(
                context, query, executionTime, isDynamic, rowsParser, queryResponse.Rows, queryResponse.Schema,
                queryResponse.CacheHit, queryResponse.ETag, queryResponse.Kind,
                queryResponse.PageToken, queryResponse.TotalBytesProcessed, queryResponse.TotalRows,
                queryResponse.JobComplete, queryResponse.JobReference)
        {
        }

        private QueryResponse(BigQueryContext context, string query, TimeSpan executionTime, bool isDynamic,
            IRowsParser rowsParser, IList<TableRow> rows, TableSchema schema, bool? cacheHit, string eTag, string kind,
            string pageToken, long? totalBytesProcessed, ulong? totalRows,
            bool? jobComplete, JobReference jobReference)
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

            TotalBytesProcessedFormatted = totalBytesProcessed.ToHumanReadableSize();

            _jobComplete = jobComplete.GetValueOrDefault(false);
            _jobReference = jobReference;
        }

        public QueryResponse<T> GetNextResponse()
        {
            var request = CreateNextPageRequest(_context, _jobComplete, _jobReference, PageToken, HasNextPage);

            var sw = Stopwatch.StartNew();
            var furtherQueryResponse = request.Execute();
            sw.Stop();

            return new QueryResponse<T>(_context, Query, sw.Elapsed, furtherQueryResponse, _isDynamic, _rowsParser);
        }

        public async Task<QueryResponse<T>> GetNextResponseAsync(CancellationToken token = default(CancellationToken))
        {
            var request = CreateNextPageRequest(_context, _jobComplete, _jobReference, PageToken, HasNextPage);

            var sw = Stopwatch.StartNew();
            var furtherQueryResponse = await TryWithRetryCountAsync(request.ExecuteAsync(token), RetryCount);
            sw.Stop();

            return new QueryResponse<T>(_context, Query, sw.Elapsed, furtherQueryResponse, _isDynamic, _rowsParser);
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
            
            var furtherRequest = context.BigQueryService.Jobs.GetQueryResults(jobReference.ProjectId, jobReference.JobId);
            furtherRequest.PageToken = pageToken;
            return furtherRequest;
        }

        /// <summary>
        /// Tries an async action that returns some data a specified number of times,
        ///  with a constant delay of 10s between each retry.
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <param name="action"></param>
        /// <param name="retrycount"></param>
        /// <returns></returns>
        private async Task<T1> TryWithRetryCountAsync<T1>(Task<T1> action, int retrycount)
        {
            const int retrydelay = 10000;
            var tries = 0;
            var success = false;
            var result = default(T1);
            do
            {
                try
                {
                    result = await action;
                    success = true;
                }
                catch
                {
                    tries++;
                    if (tries < retrycount)
                    {
                        Thread.Sleep(retrydelay);
                    }
                    else
                    {
                        throw;
                    }
                }

            } while (!success && tries < retrycount);

            return result;
        }

    }
}