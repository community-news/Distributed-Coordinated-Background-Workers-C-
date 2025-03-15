using Microsoft.EntityFrameworkCore;
using Logx.WorkerCoordination.Extensions;
using Logx.WorkerCoordination.Sample.Workers;
using Logx.WorkerCoordination.Identity;

// Replace with your cooridnation postgres db or use a different option all together
string connectionString = "Host=localhost:5438;Database=denobuladb;Username=denobula;Password=denobula";


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWorkerCoordination(options =>
    options.UseNpgsql(connectionString));


builder.Services.AddSingleton<INodeIdentityProvider, EnvironmentIdentityProvider>();

// builder.Services.AddHostedService<ClusterIndexingWorkerSingleCluster>();
// builder.Services.AddHostedService<ClusterIndexingWorkerGlobal>();
builder.Services.AddHostedService<ClusterIndexingWorkerGloballyCoordinated>();

builder.WebHost.UseUrls("http://127.0.0.1:0"); 

var app = builder.Build();

// Apply worker coordination migrations before the app starts
app.UseWorkerCoordination();
app.Run();