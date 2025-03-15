using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Logx.WorkerCoordination.Models;
using Logx.WorkerCoordination.Identity;

namespace Logx.WorkerCoordination.Workers
{
    /// <summary>
    /// A background service that runs on all pods but provides global coordination
    /// information (total workers and this worker's global index)
    /// </summary>
    public abstract class GloballyCoordinatedWorker : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _workerId;
        private readonly string _workerType;
        private readonly string _clusterName;
        private readonly string _podName;
        private readonly TimeSpan _heartbeatInterval;
        private readonly int _heartbeatTtl;
        private Timer? _heartbeatTimer;

        /// <summary>
        /// Gets the total count of active workers of this type across all clusters
        /// </summary>
        protected int GlobalWorkerCount { get; private set; } = 1;

        /// <summary>
        /// Gets the global index of this worker (0-based) among all active workers of this type
        /// </summary>
        protected int GlobalWorkerIndex { get; private set; } = 0;

        /// <summary>
        /// Initializes a new instance of the GloballyCoordinatedWorker
        /// </summary>
        protected GloballyCoordinatedWorker(
            ILogger logger,
            IServiceProvider serviceProvider,
            INodeIdentityProvider nodeIdentityProvider,
            string workerType,
            int heartbeatInvervalSeconds = 5,
            int heartbeatTtlSeconds = 15)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _workerType = workerType;
            _workerId = Guid.NewGuid().ToString();
            _clusterName = nodeIdentityProvider.GroupName;
            _podName = nodeIdentityProvider.NodeId;
            _heartbeatInterval = TimeSpan.FromSeconds(heartbeatInvervalSeconds);
            _heartbeatTtl = heartbeatTtlSeconds;
        }

        /// <summary>
        /// The main execution loop for coordinated workers
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Timestamp} Starting {WorkerType} with ID {WorkerId} on pod {PodName}",
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                _workerType, _workerId, _podName);

            // Register this worker
            await RegisterWorkerAsync();

            // Set up heartbeat timer
            _heartbeatTimer = new Timer(SendHeartbeat, null,
                _heartbeatInterval.Divide(2), _heartbeatInterval.Divide(2));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Update global worker information
                    await UpdateGlobalWorkerInfoAsync();

                    // Execute the actual worker logic
                    await ExecuteCoordinatedWorkAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in {WorkerType}", _workerType);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            // Deregister this worker
            await DeregisterWorkerAsync();
        }

        private async Task RegisterWorkerAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                var now = DateTime.UtcNow;

                var registration = new WorkerRegistration
                {
                    WorkerId = _workerId,
                    WorkerType = _workerType,
                    ClusterName = _clusterName,
                    PodName = _podName,
                    RegisteredAt = now,
                    LastHeartbeat = now
                };

                dbContext.WorkerRegistrations.Add(registration);
                await dbContext.SaveChangesAsync();

                _logger.LogInformation("{Timestamp} Worker registered successfully",
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                // Initial update of worker counts and indexes
                await UpdateGlobalWorkerInfoAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering worker");
            }
        }

        private async void SendHeartbeat(object? state)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                var now = DateTime.UtcNow;

                // Update the worker's heartbeat using EF Core
                var worker = await dbContext.WorkerRegistrations
                    .FirstOrDefaultAsync(w => w.WorkerId == _workerId);

                if (worker != null)
                {
                    worker.LastHeartbeat = now;
                    await dbContext.SaveChangesAsync();
                }

                // Every few heartbeats, check for dead workers to clean up
                if (new Random().Next(5) == 0)
                {
                    await CleanupDeadWorkersAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeat");
            }
        }

        private async Task CleanupDeadWorkersAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                // Remove workers that haven't sent a heartbeat in 1 minute
                var cutoff = DateTime.UtcNow.AddSeconds(-_heartbeatTtl);

                // Find all inactive workers
                var deadWorkers = await dbContext.WorkerRegistrations
                    .Where(w => w.LastHeartbeat < cutoff)
                    .ToListAsync();

                // Remove them if any exist
                if (deadWorkers.Any())
                {
                    dbContext.WorkerRegistrations.RemoveRange(deadWorkers);
                    var result = await dbContext.SaveChangesAsync();

                    _logger.LogInformation("{Timestamp} Cleaned up {Count} dead workers",
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), result);

                    await UpdateGlobalWorkerInfoAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up dead workers");
            }
        }

        private async Task UpdateGlobalWorkerInfoAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                // Get all active workers of this type
                var cutoff = DateTime.UtcNow.AddMinutes(-_heartbeatTtl);
                var activeWorkers = await dbContext.WorkerRegistrations
                    .Where(w => w.WorkerType == _workerType && w.LastHeartbeat > cutoff)
                    .OrderBy(w => w.WorkerId) // Sort by ID for consistent ordering
                    .ToListAsync();

                // Update counts and index
                GlobalWorkerCount = activeWorkers.Count;
                GlobalWorkerIndex = activeWorkers.FindIndex(w => w.WorkerId == _workerId);

                if (GlobalWorkerIndex < 0)
                {
                    // If our worker wasn't found in the active list, default to index 0
                    GlobalWorkerIndex = 0;
                }

                _logger.LogDebug("{Timestamp} Worker info updated: Index={Index}, Total={Total}",
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    GlobalWorkerIndex, GlobalWorkerCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating global worker info");

                // Use safe defaults
                GlobalWorkerCount = 1;
                GlobalWorkerIndex = 0;
            }
        }

        private async Task DeregisterWorkerAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                // Delete this worker's registration
                dbContext.WorkerRegistrations.RemoveRange(
                    await dbContext.WorkerRegistrations
                        .Where(w => w.WorkerId == _workerId)
                        .ToListAsync());
                

                _logger.LogInformation("{Timestamp} Worker deregistered successfully",
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deregistering worker");
            }
        }

        /// <summary>
        /// Execute work with awareness of global worker count and this worker's index
        /// </summary>
        protected abstract Task ExecuteCoordinatedWorkAsync(CancellationToken stoppingToken);

        /// <summary>
        /// Stops the worker and deregisters it
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _heartbeatTimer?.Dispose();

            // Try to deregister immediately
            Task.Run(() => DeregisterWorkerAsync()).Wait(TimeSpan.FromSeconds(5));

            _logger.LogInformation("{Timestamp} Stopping {WorkerType} with ID {WorkerId}",
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                _workerType, _workerId);
            return base.StopAsync(cancellationToken);
        }
    }
}