using System;
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
    /// A background service that ensures only one instance runs across multiple clusters
    /// using Entity Framework for global distributed locking.
    /// </summary>
    public abstract class SingleInstanceGlobalWorker : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _lockName;
        private readonly string _instanceId;
        private readonly string _clusterName;
        private readonly string _podName;
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);
        private Timer? _heartbeatTimer;
        private bool _isLeader = false;

        /// <summary>
        /// Initializes a new instance of the SingleInstanceGlobalWorker
        /// </summary>
        protected SingleInstanceGlobalWorker(
            ILogger logger,
            IServiceProvider serviceProvider,
            INodeIdentityProvider nodeIdentityProvider,
            string workerName)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _lockName = $"global-lock:{workerName}";
            _instanceId = Guid.NewGuid().ToString();
            _clusterName = nodeIdentityProvider.GroupName;
            _podName = nodeIdentityProvider.NodeId;
        }

        /// <summary>
        /// The main execution loop that ensures only one instance runs globally
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{Timestamp} Starting {WorkerType} on pod {PodName} in cluster {ClusterName}",
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                GetType().Name, _podName, _clusterName);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_isLeader)
                    {
                        await TryBecomeLeaderAsync();
                    }

                    if (_isLeader)
                    {
                        await ExecuteGlobalLeaderWorkAsync(stoppingToken);
                    }
                    else
                    {
                        _logger.LogDebug("Not the global leader, waiting...");
                        await Task.Delay(5000, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in {WorkerType}", GetType().Name);
                    await Task.Delay(5000, stoppingToken);
                }
            }

            if (_isLeader)
            {
                await ReleaseLeadershipAsync();
            }
        }

        private async Task TryBecomeLeaderAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

            var now = DateTime.UtcNow;
            var expiration = now.Add(_heartbeatInterval.Add(_heartbeatInterval)); // Double interval for safety

            try
            {
                // Begin transaction
                await using var transaction = await dbContext.Database.BeginTransactionAsync();

                // Try to find the global lock with update lock hint
                var globalLock = dbContext.WorkerLocks
                    .Where(w => w.LockName == _lockName)
                    .FirstOrDefault();


                bool acquired = false;

                if (globalLock == null)
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
                else if (globalLock.ExpiresAt < now)
                {
                    // Lock exists but has expired, take it over
                    globalLock.OwnerId = _instanceId;
                    globalLock.OwnerPod = _podName;
                    globalLock.ClusterName = _clusterName;
                    globalLock.ExpiresAt = expiration;
                    globalLock.LastRenewed = now;
                    await dbContext.SaveChangesAsync();
                    acquired = true;
                }

                // Commit transaction
                await transaction.CommitAsync();

                _isLeader = acquired;

                if (acquired && _heartbeatTimer == null)
                {
                    _logger.LogInformation("{Timestamp} Acquired global leadership lock for worker {WorkerName}",
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        _lockName.Replace("global-lock:", ""));

                    // Set up heartbeat timer to maintain leadership
                    _heartbeatTimer = new Timer(SendHeartbeat, null,
                        _heartbeatInterval.Divide(2), _heartbeatInterval.Divide(2));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying to become leader");
                _isLeader = false;
            }
        }

        /// <summary>
        /// Sends a heartbeat to renew the lock and maintain leadership
        /// </summary>
        private async void SendHeartbeat(object? state)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                var now = DateTime.UtcNow;
                var expiration = now.Add(_heartbeatInterval.Add(_heartbeatInterval));

                // Update the lock with a new expiration time

                var result = dbContext.WorkerLocks
                    .Where(w => w.LockName == _lockName && w.OwnerId == _instanceId)
                    .ExecuteUpdate(w => w.SetProperty(x => x.ExpiresAt, expiration)
                        .SetProperty(x => x.LastRenewed, now));


                if (result == 0)
                {
                    _logger.LogWarning("{Timestamp} Failed to update heartbeat, global leadership lost",
                        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
                    _isLeader = false;
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeat");
                _isLeader = false;
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
            }
        }

        private async Task ReleaseLeadershipAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<WorkerCoordinationDbContext>();

                // Delete the lock if we own it
                await dbContext.WorkerLocks
                    .Where(w => w.LockName == _lockName && w.OwnerId == _instanceId)
                    .ExecuteDeleteAsync();


                _isLeader = false;
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;

                _logger.LogInformation("{Timestamp} Released global leadership lock",
                    DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing global leadership");
            }
        }

        /// <summary>
        /// Execute work that should only be done by the global leader instance
        /// </summary>
        protected abstract Task ExecuteGlobalLeaderWorkAsync(CancellationToken stoppingToken);

        /// <summary>
        /// Stops the worker and releases the lock
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _heartbeatTimer?.Dispose();

            // Try to release leadership immediately
            if (_isLeader)
            {
                Task.Run(() => ReleaseLeadershipAsync()).Wait(TimeSpan.FromSeconds(5));
            }

            return base.StopAsync(cancellationToken);
        }
    }
}