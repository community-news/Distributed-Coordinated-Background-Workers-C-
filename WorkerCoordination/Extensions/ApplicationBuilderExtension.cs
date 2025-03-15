using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Logx.WorkerCoordination.Models;

namespace Logx.WorkerCoordination.Extensions
{
    /// <summary>
    /// Extension methods for IApplicationBuilder to configure worker coordination
    /// </summary>
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        /// Initializes the Worker Coordination database and applies any pending migrations
        /// </summary>
        /// <param name="app">The application builder</param>
        /// <param name="logToConsole">Whether to log migration information to console (default: true)</param>
        /// <returns>The application builder for method chaining</returns>
        public static IApplicationBuilder UseWorkerCoordination(
            this IApplicationBuilder app,
            bool logToConsole = true)
        {
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var logger = scope.ServiceProvider.GetService<ILogger<WorkerCoordinationDbContext>>();
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                try
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();
                    // Create tables if they don't exist
                    CreateTablesIfNotExists(dbContext, logToConsole);

                    // Check if there are pending migrations
                    var pendingMigrations = dbContext.Database.GetPendingMigrations();

                    if (pendingMigrations.Any())
                    {
                        var migrationCount = pendingMigrations.Count();
                        var message = $"[{timestamp}] Applying {migrationCount} pending worker coordination migrations...";

                        if (logToConsole)
                            Console.WriteLine(message);
                        logger?.LogInformation(message);

                        // Apply the migrations
                        dbContext.Database.Migrate();

                        message = $"[{timestamp}] Worker coordination migrations successfully applied.";
                        if (logToConsole)
                            Console.WriteLine(message);
                        logger?.LogInformation(message);
                    }
                    else
                    {
                        var message = $"[{timestamp}] No new worker coordination migrations to apply.";
                        if (logToConsole)
                            Console.WriteLine(message);
                        logger?.LogInformation(message);
                    }

                    // Ensure the database is created (useful for in-memory databases or first run)
                    dbContext.Database.EnsureCreated();
                }
                catch (Exception ex)
                {
                    var message = $"[{timestamp}] Worker coordination migration error: {ex.Message}";
                    if (logToConsole)
                        Console.Error.WriteLine(message);
                    logger?.LogError(ex, "Worker coordination migration error");

                    // Re-throw the exception to fail startup if migrations cannot be applied
                    throw;
                }
            }

            return app;
        }

        private static void CreateTablesIfNotExists(WorkerCoordinationDbContext dbContext, bool logToConsole = true)
        {
            bool tablesExist = false;
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            try
            {
                // Try to query the WorkerLocks table
                // If it succeeds, tables likely exist
                _ = dbContext.WorkerLocks.Any();
                tablesExist = true;
            }
            catch
            {
                // Table doesn't exist
                tablesExist = false;
            }

            if (!tablesExist)
            {
                if (logToConsole)
                    Console.WriteLine($"[{timestamp}] Tables not found. Creating schema...");

                // Create database schema
                dbContext.Database.EnsureDeleted();
                dbContext.Database.EnsureCreated();

                if (logToConsole)
                    Console.WriteLine($"[{timestamp}] Schema created successfully by king");

                // check if workerlogs exist now
                try
                {
                    _ = dbContext.WorkerLocks.Any();
                    tablesExist = true;
                }
                catch
                {
                    // Table doesn't exist
                    tablesExist = false;
                }

                if (tablesExist)
                {
                    if (logToConsole)
                        Console.WriteLine($"[{timestamp}] Tables created successfully");
                }
                else
                {
                    if (logToConsole)
                        Console.WriteLine($"[{timestamp}] Failed to create tables");
                }
            }
            else
            {
                if (logToConsole)
                    Console.WriteLine($"[{timestamp}] Tables already exist");
            }
        }
    }
}