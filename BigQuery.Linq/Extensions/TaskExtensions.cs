using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BigQuery.Linq.Extensions
{

    public static class TaskExtensions
    {

        public static async Task<T1> TryWithRetryCountAsync<T1>(this Func<Task<T1>> taskCreator, int retryCount = 50, int retryDelay = 10000)
        {
            var tries = 0;
            var success = false;
            var result = default(T1);
            do
            {
                try
                {
                    var task = taskCreator.Invoke();
                    result = await task;
                    success = true;
                }
                catch (Exception ex)
                {
                    tries++;
                    if (tries < retryCount)
                    {
                        Trace.TraceError("Query task threw exception, retry " + tries + " / " + retryCount + "\n" + ex.Message + "\n" + ex.StackTrace);
                        Thread.Sleep(retryDelay);
                    }
                    else
                    {
                        Trace.TraceError("Query task threw exception, all " + retryCount + " retries used." + "\n" + ex.Message + "\n" + ex.StackTrace);
                        throw;
                    }
                }

            } while (!success && tries <= retryCount);

            return result;
        }
    }
}
