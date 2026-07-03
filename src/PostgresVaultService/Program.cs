var builder = Host.CreateApplicationBuilder(args);

builder.AddStructuredLogging();

builder.Services.Configure<VaultOptions>(builder.Configuration.GetSection(VaultOptions.SectionName));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection(PostgresOptions.SectionName));
builder.Services.AddApplicationResiliencePipelines(builder.Configuration);

builder.Services.AddSingleton<IVaultSecretProvider, VaultSecretProvider>();
builder.Services.AddSingleton<DynamicPostgresConnectionFactory>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DynamicPostgresConnectionFactory>());
builder.Services.AddHostedService<InsertWorker>();

var host = builder.Build();
await host.RunWithStructuredLoggingAsync();
