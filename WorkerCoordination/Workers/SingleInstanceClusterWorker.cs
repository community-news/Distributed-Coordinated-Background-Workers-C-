using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Logx.WorkerCoordination.Models;
using Logx.WorkerCoordination.Identity;

namespace Logx.WorkerCoordination.Workers
{
    /// <summary>
    /// A background service that ensures only one instance runs within a Kubernetes cluster
    /// using Entity Framework for distributed locking.
    /// </summary>
    public abstract class SingleInstanceClusterWorker : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _lockName;
        private readonly string _instanceId;
        private readonly string _clusterName;
        private readonly string _podName;
        private readonly TimeSpan _lockTtl = TimeSpan.FromSeconds(30);
        private Timer? _renewTimer;
        private bool _hasLock = false;

        /// <summary>
        /// Initializes a new instance of the SingleInstanceClusterWorker
        /// </summary>
        protected SingleInstanceClusterWorker(
            ILogger logger,
            IServiceProvider serviceProvider,
            INodeIdentityProvider nodeIdentityProvider,
            string workerName)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _instanceId = Guid.NewGuid().ToString();
            _clusterName = nodeIdentityProvider.GroupName;
            
            // since this is cluster specific, we use the custer name as part of the lock name
            _lockName = $"cluster-lock:{workerName}:{_clusterName}";
            _podName = nodeIdentityProvider.NodeId;
        }

        /// <summary>
        /// The main execution loop that ensures only one instance runs per cluster
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Timestamp} Starting {WorkerType} on pod {PodName}",
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), GetType().Name, _podName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Try to acquire the lock if we don't have it
                    if (!_hasLock)
                    {
                        _hasLock = await TryAcquireLockAsync();
                    }

                    if (_hasLock)
                    {
                        // If we have the lock, perform the work
                        await ExecuteLeaderWorkAsync(stoppingToken);
                    }
                    else
                    {
                        _logger.LogDebug("Not the leader, waiting...");
                        await Task.Delay(5000, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in {WorkerType}", GetType().Name);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            // Release the lock if we have it
            if (_hasLock)
            {
                await ReleaseLockAsync();
            }
        }

        private async Task<bool> TryAcquireLockAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

            var now = DateTime.UtcNow;
            var expiration = now.AddSeconds(_lockTtl.TotalSeconds);

            try
            {
                // Begin transaction
                await using var transaction = await dbContext.Database.BeginTransactionAsync();

                // Try to find an existing lock
                var existingLock = await dbContext.WorkerLocks
                    .FirstOrDefaultAsync(l => l.LockName == _lockName);

                bool acquired = false;

                if (existingLock == null)
                {
                    // No lock exists, create a new one
                    dbContext.WorkerLocks.Add(new WorkerLock
                    {
                        LockName = _lockName,
                        OwnerId = _instanceId,
                        OwnerPod = _podName,
                        ClusterName = _clusterName,
                        ExpiresAt = expiration,
                        LastRenewed = now
                    });
                    await dbContext.SaveChangesAsync();
                    acquired = true;
                }
                else if (existingLock.ExpiresAt < now)
                {
                    // Lock exists but has expired, take it over
                    existingLock.OwnerId = _instanceId;
                    existingLock.OwnerPod = _podName;
                    existingLock.ClusterName = _clusterName;
                    existingLock.ExpiresAt = expiration;
                    existingLock.LastRenewed = now;
                    await dbContext.SaveChangesAsync();
                    acquired = true;
                }

                // Commit transaction
                await transaction.CommitAsync();

                if (acquired)
                {
                    _logger.LogInformation("{Timestamp} Acquired leadership lock for cluster {ClusterName}",
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), _clusterName);

                    // Set up a timer to renew the lock periodically
                    _renewTimer = new Timer(RenewLock, null,
                        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
                }

                return acquired;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying to acquire lock");
                return false;
            }
        }

        private async void RenewLock(object? state)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                var now = DateTime.UtcNow;
                var expiration = now.AddSeconds(_lockTtl.TotalSeconds);

                // Find and update the lock using EF Core
                var lockToRenew = await dbContext.WorkerLocks
                    .FirstOrDefaultAsync(l => l.LockName == _lockName && l.OwnerId == _instanceId);


                if (lockToRenew != null)
                {
                    lockToRenew.ExpiresAt = expiration;
                    lockToRenew.LastRenewed = now;

                    var result = await dbContext.SaveChangesAsync();

                    if (result == 0)
                    {
                        _logger.LogWarning("{Timestamp} Failed to renew lock, leadership lost",
                            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                        _hasLock = false;
                        _renewTimer?.Dispose();
                        _renewTimer = null;
                    }
                }
                else
                {
                    _logger.LogWarning("{Timestamp} Lock not found during renewal, leadership lost",
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                    _hasLock = false;
                    _renewTimer?.Dispose();
                    _renewTimer = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renewing lock");
                _hasLock = false;
                _renewTimer?.Dispose();
                _renewTimer = null;
            }
        }

        private async Task ReleaseLockAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                // Find and remove the lock using EF Core
                var lockToRemove = await dbContext.WorkerLocks
                    .FirstOrDefaultAsync(l => l.LockName == _lockName && l.OwnerId == _instanceId);

                if (lockToRemove != null)
                {
                    dbContext.WorkerLocks.Remove(lockToRemove);
                    await dbContext.SaveChangesAsync();
                }

                _hasLock = false;
                _renewTimer?.Dispose();
                _renewTimer = null;

                _logger.LogInformation("{Timestamp} Released leadership lock",
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing lock");
            }
        }

        /// <summary>
        /// Execute work that should only be done by the leader instance
        /// </summary>
        protected abstract Task ExecuteLeaderWorkAsync(CancellationToken stoppingToken);

        /// <summary>
        /// Stops the worker and releases the lock
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _renewTimer?.Dispose();

            // Try to release the lock immediately before stopping
            if (_hasLock)
            {
                Task.Run(() => ReleaseLockAsync()).Wait(TimeSpan.FromSeconds(5));
            }

            return base.StopAsync(cancellationToken);
        }
    }
}