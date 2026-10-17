using VpnClient.Service;

var builder = Host.CreateApplicationBuilder(args);

// En service Windows : nom du service + logs dans l'Observateur d'événements.
// Lancé en console (admin) : fonctionne tel quel, pratique pour le développement.
builder.Services.AddWindowsService(o => o.ServiceName = "VpnClientService");
builder.Services.Configure<VpnOptions>(builder.Configuration.GetSection("Vpn"));
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<TunnelManager>();
builder.Services.AddHostedService<PipeServer>();

builder.Build().Run();
