using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pomelo.EntityFrameworkCore.MySql.IntegrationTests.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pomelo.EntityFrameworkCore.MySql.Tests;

namespace Pomelo.EntityFrameworkCore.MySql.IntegrationTests.Commands
{

    public class TestPerformanceCommand : ITestPerformanceCommand
    {

        private AppDb _db;

        public TestPerformanceCommand(AppDb db)
        {
            _db = db;
        }

        private Lazy<ServiceProvider> _serviceProvider = new Lazy<ServiceProvider>(() =>
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection
                .AddLogging(builder =>
                    builder
                        .AddConfiguration(AppConfig.Config.GetSection("Logging"))
                        .AddConsole()
                )
                .AddScoped<ITestPerformanceRunner, TestPerformanceRunner>();
            Startup.ConfigureEntityFramework(serviceCollection);

            return serviceCollection.BuildServiceProvider();
        });

        public void Run(int iterations, int concurrency, int ops)
        {
            if (iterations <= 0 || concurrency <= 0 || ops <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(iterations),
                    "testPerformance iterations, concurrency, and operations must be positive integers.");
            }

            Console.WriteLine("Testing with EF_BATCH_SIZE=" + AppConfig.EfBatchSize);
            Console.WriteLine();

            var recordNum = 0;
            async Task insert1(AppDb db)
            {
                var blog = new Blog
                {
                    Title = "test " + Interlocked.Increment(ref recordNum)
                };
                db.Blogs.Add(blog);
                await db.SaveChangesAsync();
            }

            var selected = new ConcurrentQueue<string>();
            async Task select1(AppDb db)
            {
                var blog = await db.Blogs.Skip(selected.Count).Take(1).OrderBy(m => m.Id).FirstOrDefaultAsync();
                if (blog != null)
                {
                    selected.Enqueue(blog.Title);
                }
            }

            var updatedNum = 0;
            async Task update1(AppDb db)
            {
                var blog = await db.Blogs.Skip(selected.Count).Take(1).OrderBy(m => m.Id).FirstOrDefaultAsync();
                if (blog != null)
                {
                    blog.Title = "updated " + Interlocked.Increment(ref updatedNum);
                }
                await db.SaveChangesAsync();
            }

            var sleepNum = 0;
            async Task sleepMillisecond(AppDb db)
            {
                await db.Database.ExecuteSqlRawAsync("SELECT SLEEP(0.001)");
                db.Database.CloseConnection();
                Interlocked.Increment(ref sleepNum);
            }

            async Task insert100(AppDb db)
            {
                for (var i = 0; i < 100; i++)
                {
                    var blog = new Blog
                    {
                        Title = "test " + Interlocked.Increment(ref recordNum)
                    };
                    db.Blogs.Add(blog);
                }
                await db.SaveChangesAsync();
            }

            async Task update100(AppDb db)
            {
                var blogs = await db.Blogs.Skip(updatedNum).Take(100).OrderBy(m => m.Id).ToListAsync();
                foreach (var blog in blogs)
                {
                    blog.Title = "updated " + Interlocked.Increment(ref updatedNum);
                }
                await db.SaveChangesAsync();
            }

            async Task select100(AppDb db)
            {
                var blogs = await db.Blogs.Skip(selected.Count).Take(100).OrderBy(m => m.Id).ToListAsync();
                foreach (var blog in blogs)
                {
                    selected.Enqueue(blog.Title);
                }
            }

#pragma warning disable EF1003 // Table name comes from model metadata, not user input.
            _db.Database.ExecuteSqlRaw("DELETE FROM `" + _db.Model.FindEntityType(typeof(Blog)).GetTableName() + "`");
#pragma warning restore EF1003

            var insertCount = 0;
            PerfTest(insert1, "Insert 1", iterations, concurrency, ops, () => insertCount = _db.Blogs.Count()).GetAwaiter().GetResult();
            insertCount = _db.Blogs.Count() - insertCount;
            Console.WriteLine("Records Inserted: " + insertCount);
            Console.WriteLine();

            var updateCount = 0;
            PerfTest(update1, "Update 1", iterations, concurrency, ops, () => updateCount = updatedNum).GetAwaiter().GetResult();
            updateCount = updatedNum - updateCount;
            Console.WriteLine("Records Updated: " + updateCount);
            Console.WriteLine();

            var selectCount = 0;
            PerfTest(select1, "Select 1", iterations, concurrency, ops, () => selectCount = selected.Count).GetAwaiter().GetResult();
            selectCount = selected.Count - selectCount;
            Console.WriteLine("Records Selected: " + selectCount);
            if (selected.TryPeek(out var firstRecord))
            {
                Console.WriteLine("First Record: " + firstRecord);
            }

            Console.WriteLine();

            var sleepCount = 0;
            PerfTest(sleepMillisecond, "Sleep 1ms", iterations, concurrency, ops, () => sleepCount = sleepNum).GetAwaiter().GetResult();
            Console.WriteLine("Total Sleep Commands: " + (sleepNum - sleepCount));
            Console.WriteLine();

            PerfTest(insert100, "Insert 100", iterations, concurrency, 1, () => insertCount = _db.Blogs.Count()).GetAwaiter().GetResult();
            insertCount = _db.Blogs.Count() - insertCount;
            Console.WriteLine("Records Inserted: " + insertCount);
            Console.WriteLine();

            PerfTest(update100, "Update 100", iterations, concurrency, 1, () => updateCount = updatedNum).GetAwaiter().GetResult();
            updateCount = updatedNum - updateCount;
            Console.WriteLine("Records Updated: " + updateCount);
            Console.WriteLine();

            PerfTest(select100, "Select 100", iterations, concurrency, 1, () => selectCount = selected.Count).GetAwaiter().GetResult();
            selectCount = selected.Count - selectCount;
            Console.WriteLine("Records Selected: " + selectCount);
            Console.WriteLine();
        }

        public Task PerfTest(Func<AppDb, Task> test, string testName, int iterations, int concurrency, int ops)
        {
            return PerfTest(test, testName, iterations, concurrency, ops, null);
        }

        private async Task PerfTest(Func<AppDb, Task> test, string testName, int iterations, int concurrency, int ops, Action afterWarmup)
        {
            var timers = new List<TimeSpan>();
            var allocationResults = new List<long>();
            var retainedHeapResults = new List<long>();

            async Task ExecuteIteration()
            {
                var tasks = new List<Task>();
                for (var connection = 0; connection < concurrency; connection++)
                {
                    var scope = _serviceProvider.Value.CreateScope();
                    var testPerformanceRunner = scope.ServiceProvider.GetService<ITestPerformanceRunner>();
                    tasks.Add(ExecuteConnection(scope, testPerformanceRunner));
                }

                await Task.WhenAll(tasks);
            }

            async Task ExecuteConnection(IServiceScope scope, ITestPerformanceRunner testPerformanceRunner)
            {
                try
                {
                    await testPerformanceRunner.ConnectionTask(test, ops);
                }
                finally
                {
                    scope.Dispose();
                }
            }

            const int warmupIterations = 1;
            await ExecuteIteration();
            afterWarmup?.Invoke();

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var allocatedBefore = GC.GetTotalAllocatedBytes(true);
                var stopwatch = Stopwatch.StartNew();
                await ExecuteIteration();
                stopwatch.Stop();

                var allocatedAfter = GC.GetTotalAllocatedBytes(true);
                timers.Add(stopwatch.Elapsed);
                allocationResults.Add(allocatedAfter - allocatedBefore);
                retainedHeapResults.Add(GC.GetTotalMemory(true));
            }

            Console.WriteLine("Test:                     " + testName);
            Console.WriteLine("Warmup iterations:        " + warmupIterations);
            Console.WriteLine("Measured iterations:      " + iterations);
            Console.WriteLine("Concurrency:              " + concurrency);
            Console.WriteLine("Operations:               " + ops);
            var orderedTimers = timers.OrderBy(timer => timer).ToArray();
            var medianTime = orderedTimers.Length % 2 == 0
                ? TimeSpan.FromTicks(
                    (orderedTimers[orderedTimers.Length / 2 - 1].Ticks + orderedTimers[orderedTimers.Length / 2].Ticks) / 2)
                : orderedTimers[orderedTimers.Length / 2];
            Console.WriteLine("Times (Min, Median, Average, Max) "
                              + timers.Min() + ", "
                              + medianTime + ", "
                              + TimeSpan.FromTicks(timers.Sum(timer => timer.Ticks) / timers.Count) + ", "
                              + timers.Max());

            var orderedAllocations = allocationResults.OrderBy(allocation => allocation).ToArray();
            var medianAllocation = orderedAllocations.Length % 2 == 0
                ? (orderedAllocations[orderedAllocations.Length / 2 - 1] + orderedAllocations[orderedAllocations.Length / 2]) / 2D
                : orderedAllocations[orderedAllocations.Length / 2];
            var allocationSummary =
                $"{allocationResults.Min():N0}, {medianAllocation:N0}, "
                + $"{allocationResults.Average():N0}, {allocationResults.Max():N0} bytes";
            Console.WriteLine("Managed allocation deltas (Min, Median, Average, Max) "
                              + allocationSummary);

            const double bytesPerMib = 1024 * 1024;
            var orderedRetainedHeap = retainedHeapResults.OrderBy(memory => memory).ToArray();
            var medianRetainedHeap = orderedRetainedHeap.Length % 2 == 0
                ? (orderedRetainedHeap[orderedRetainedHeap.Length / 2 - 1] + orderedRetainedHeap[orderedRetainedHeap.Length / 2]) / 2D
                : orderedRetainedHeap[orderedRetainedHeap.Length / 2];
            var retainedHeapSummary =
                $"{retainedHeapResults.Min() / bytesPerMib:F2}, {medianRetainedHeap / bytesPerMib:F2}, "
                + $"{retainedHeapResults.Average() / bytesPerMib:F2}, {retainedHeapResults.Max() / bytesPerMib:F2} MiB";
            Console.WriteLine("Retained managed heap after measured iteration (Min, Median, Average, Max) "
                              + retainedHeapSummary);
            Console.WriteLine();
        }

    }

}
