using Microsoft.Extensions.Logging;
using Logx.WorkerCoordination.Workers;
using Logx.WorkerCoordination.Identity;

namespace Logx.WorkerCoordination.Sample.Workers
{
    // Example 1: Single pod per cluster
    public class ClusterIndexingWorkerSingleCluster : SingleInstanceClusterWorker
    {
        private readonly ILogger<ClusterIndexingWorkerSingleCluster> _logger;

        public ClusterIndexingWorkerSingleCluster(
            ILogger<ClusterIndexingWorkerSingleCluster> logger,
            IServiceProvider serviceProvider,
            INodeIdentityProvider nodeIdentityProvider)
            : base(logger, serviceProvider, nodeIdentityProvider, "sample-worker")
        {
            _logger = logger;
        }

        protected override async Task ExecuteLeaderWorkAsync(CancellationToken stoppingToken)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            _logger.LogInformation("[{timestamp}] Hello World! I am the cluster leader!", timestamp);

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    // Example 2: Single pod across all clusters
    public class ClusterIndexingWorkerGlobal : SingleInstanceGlobalWorker
    {
        private readonly ILogger<ClusterIndexingWorkerGlobal> _logger;

        public ClusterIndexingWorkerGlobal(
            ILogger<ClusterIndexingWorkerGlobal> logger,
            INodeIdentityProvider nodeIdentityProvider,
            IServiceProvider serviceProvider)
            : base(logger, serviceProvider, nodeIdentityProvider, "sample-worker")
        {
            _logger = logger;
        }

        protected override async Task ExecuteGlobalLeaderWorkAsync(CancellationToken stoppingToken)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            _logger.LogInformation("[{timestamp}] Hello World! I am the global leader!", timestamp);

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    // Example 3: All pods coordinated globally
    public class ClusterIndexingWorkerGloballyCoordinated : GloballyCoordinatedWorker
    {
        private readonly ILogger<ClusterIndexingWorkerGloballyCoordinated> _logger;

        // by default, the worker will emit a heartbeat every 5 seconds
        // if a worker has not emitted a heartbeat for 15 seconds, it will be considered dead
        // you may override this in the constructor by passing a different value on the base class 

        public ClusterIndexingWorkerGloballyCoordinated(
            ILogger<ClusterIndexingWorkerGloballyCoordinated> logger,
            INodeIdentityProvider nodeIdentityProvider,
            IServiceProvider serviceProvider)
            : base(logger, serviceProvider, nodeIdentityProvider, "sample-worker")
        {
            _logger = logger;
        }

        protected override async Task ExecuteCoordinatedWorkAsync(CancellationToken stoppingToken)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            _logger.LogInformation("[{timestamp}] Hello World! I am a global worker! I am {me} out of {all} workers!", timestamp, GlobalWorkerIndex + 1, GlobalWorkerCount);

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

}