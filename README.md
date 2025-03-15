# Logx.WorkerCoordination (Distributed Coordinated Workers)

[![NuGet](https://img.shields.io/nuget/v/Logx.WorkerCoordination.svg)](https://www.nuget.org/packages/Logx.WorkerCoordination/)
[![License](https://img.shields.io/badge/license-Unlicensed-green.svg)](https://github.com/sooryakiran/Logx.WorkerCoordination/blob/main/LICENSE)

A lightweight, database-backed coordination library for distributed .NET workers that provides leader election, worker synchronization, and global coordination across distributed environments.

Who is this for:
1. If spark jobs are an overkill.
2. If a single threaded worker is too slow.
3. If you do not want a complicated infra or queuing service.
   
## Features

- **Single-Instance Execution**: Ensure only one worker runs in a cluster/group
- **Global Leader Election**: Elect a single worker across all clusters/groups
- **Worker Coordination**: Distribute workload across multiple workers
- **Database Agnostic**: Works with any Entity Framework Core provider
- **Environment Agnostic**: Functions in any distributed environment (Kubernetes, VMs, on-premise)
- **Resilient**: Handles node failures and network partitions
- **Lightweight**: Minimal dependencies and low overhead
- **Easy Integration**: Simple extension methods for ASP.NET Core and Worker Services

## Installation

TODO

<!-- ```bash
dotnet add package Logx.WorkerCoordination
``` -->

## Quick Start

```C#
// Add worker coordination to your service collection
builder.Services.AddWorkerCoordination(options =>
    options.UseNpgsql("Host=localhost;Database=worker_coordination;Username=user;Password=pw"));

// Enable database setup
app.UseWorkerCoordination();

// Add your coordinated worker
builder.Services.AddHostedService<MyClusterWorker>();
```

### Worker Types

The library provides three base worker types to handle different coordination scenarios:

#### 1. Single Instance Per Cluster (SingleInstanceClusterWorker)

Ensures that only one worker instance runs in each cluster/group.

```C#
public class IndexingWorker : SingleInstanceClusterWorker
{
    public IndexingWorker(ILogger<IndexingWorker> logger,
                         IServiceProvider serviceProvider,
                         INodeIdentityProvider nodeIdentityProvider)
        : base(logger, serviceProvider, nodeIdentityProvider, "indexing-worker")
    {
    }

    protected override async Task ExecuteLeaderWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executing as the cluster leader");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}
```

#### 2. Single Instance Globally (SingleInstanceGlobalWorker)

Ensures that only one worker instance runs across all clusters/groups.

```C#
public class GlobalTaskWorker : SingleInstanceGlobalWorker
{
    public GlobalTaskWorker(ILogger<GlobalTaskWorker> logger,
                           INodeIdentityProvider nodeIdentityProvider,
                           IServiceProvider serviceProvider)
        : base(logger, serviceProvider, nodeIdentityProvider, "global-task")
    {
    }

    protected override async Task ExecuteGlobalLeaderWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Executing as the global leader");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}
```

#### 3. Globally Coordinated (GloballyCoordinatedWorker)

All instances run but coordinate work distribution.

```C#
public class DataProcessingWorker : GloballyCoordinatedWorker
{
    public DataProcessingWorker(ILogger<DataProcessingWorker> logger,
                               INodeIdentityProvider nodeIdentityProvider,
                               IServiceProvider serviceProvider)
        : base(logger, serviceProvider, nodeIdentityProvider, "data-processor")
    {
    }

    protected override async Task ExecuteCoordinatedWorkAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {Index} of {Total} processing data",
            GlobalWorkerIndex + 1, GlobalWorkerCount);

        // Process work for this worker's share
        var myPartition = GlobalWorkerIndex;
        var totalPartitions = GlobalWorkerCount;

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}
```

## Node Identity

By default, the library detects the node's identity from the environment. You can customize this behavior:

```C#
// Use the default environment-based identity provider
builder.Services.AddSingleton<INodeIdentityProvider, EnvironmentIdentityProvider>();

// OR provide explicit identity
builder.Services.AddSingleton<INodeIdentityProvider>(new EnvironmentIdentityProvider(
    "my-worker-node-1", "production-group"));

// OR use custom environment variable names
builder.Services.AddSingleton<INodeIdentityProvider>(new EnvironmentIdentityProvider(
    nodeIdEnvVar: "MY_NODE_ID",
    groupNameEnvVar: "DEPLOYMENT_GROUP"));
```

Environment idenity profiver by default uses these Kubernetes compatible env variables:

1. `HOSTNAME` for node id within cluster,
2. `CLUSTER_NAME`

## Complete Example

Refer to `WorkerCoordintation.Sample`

## Advanced Configuration

### 1. Use a different Database provider other than Postgres

#### a. PostgreSQL

```C#
// Add package: Npgsql.EntityFrameworkCore.PostgreSQL
builder.Services.AddWorkerCoordination(options =>
    options.UseNpgsql("Host=localhost;Database=worker_coordination;Username=user;Password=pw",
    npgsqlOptions => npgsqlOptions.EnableRetryOnFailure()));
```

#### b. SQL Server

```C#
// Add package: Microsoft.EntityFrameworkCore.SqlServer
builder.Services.AddWorkerCoordination(options =>
    options.UseSqlServer("Server=myServerAddress;Database=worker_coordination;User Id=user;Password=pw;",
    sqlOptions => sqlOptions.EnableRetryOnFailure()));
```

#### c.Sqlite (How are you gonna share the same sqlite file across multiple instances? Anyhow)

````C#
// Add package: Microsoft.EntityFrameworkCore.Sqlite
builder.Services.AddWorkerCoordination(options =>
    options.UseSqlite("Data Source=worker_coordination.db"));
    ```
````
#### Not Satisfied?
Refere here: https://learn.microsoft.com/en-us/ef/core/providers/?tabs=dotnet-core-cli
