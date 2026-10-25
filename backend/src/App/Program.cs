using App.Analytics;
using App.Analytics.Data;
using App.Api.Hosting;
using App.Capture;
using App.Capture.Data;
using App.Connections;
using App.Connections.Data;
using App.Discovery;
using App.Discovery.Data;
using App.Governance;
using App.Governance.Data;
using App.Intelligence;
using App.Intelligence.Data;
using App.Knowledge;
using App.Knowledge.Data;
using App.Processing;
using App.Processing.Data;
using App.Recommendations;
using App.Recommendations.Data;
using App.Shared;
using App.Shared.Data;

// The composition root: the only place that knows every module's implementation.
var builder = WebApplication.CreateBuilder(args);
builder.AddHostPlumbing();

IModule[] modules =
[
    new DiscoveryModule(),
    new ConnectionsModule(),
    new CaptureModule(),
    new ProcessingModule(),
    new IntelligenceModule(),
    new RecommendationsModule(),
    new KnowledgeModule(),
    new AnalyticsModule(),
    new GovernanceModule(),
];
foreach (var module in modules)
    builder.Services.AddSingleton(module);

// Each module's data context, in its own schema of the one database; migrated in this order at start-up.
builder.Services
    .AddModuleDbContext<DiscoveryDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<ConnectionsDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<CaptureDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<ProcessingDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<IntelligenceDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<RecommendationsDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<KnowledgeDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<AnalyticsDbContext>(DatabaseOptions.ConnectionString)
    .AddModuleDbContext<GovernanceDbContext>(DatabaseOptions.ConnectionString);

// The services modules offer one another, the connectors that feed Capture, and Capture's worker that hands
// events to Processing.
builder.Services
    .AddConnectors()
    .AddCapture()
    .AddProcessing();

var app = builder.Build();
app.UseHostPlumbing();

app.Run();
