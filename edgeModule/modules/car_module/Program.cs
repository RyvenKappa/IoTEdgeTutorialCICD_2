using car_module;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>services.AddHostedService<ModuleBackgroundService>())
    .Build();

host.Run();