using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Transport.Mqtt;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace car_module;

public class ModuleBackgroundService : BackgroundService
{
    private ModuleClient? _moduleClient;
    private readonly ILogger<ModuleBackgroundService> _logger;
    private CancellationToken _cancellationToken;

    private const double MaxSpeed = 100.0;
    private const double MinSpeed = 20.0;
    private double currentSpeed = 0;
    private double currentAcceleration = 0;
    private double fuelLevel = 100;
    private int telemetryInterval = 1;
    private readonly Random random = new();
    private double totalDistance = 0;

    public ModuleBackgroundService(ILogger<ModuleBackgroundService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _cancellationToken = stoppingToken;

        var mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
        var settings = new ITransportSettings[] { mqttSetting };

        _moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
        _moduleClient.SetConnectionStatusChangesHandler((status, reason) =>
            _logger.LogWarning("Connection changed: {status}, {reason}", status, reason));
        await _moduleClient.OpenAsync(stoppingToken);

        _logger.LogInformation("ModuleClient initialized and connected to IoT Edge runtime.");

        await _moduleClient.SetMethodHandlerAsync("SetTelemetryInterval", SetTelemetryIntervalHandler, null, stoppingToken);

        _ = SimulateVehicleAsync(); // Fire-and-forget loop
    }

    private async Task SimulateVehicleAsync()
    {
        while (!_cancellationToken.IsCancellationRequested)
        {
            AdjustSpeed();

            var telemetry = new
            {
                vehicleId = "0udU6",
                name = "Toyota Corolla",
                plate = "ABC-1234",
                speed = Math.Round(currentSpeed, 2),
                acceleration = Math.Round(currentAcceleration, 2),
                fuel = Math.Round(fuelLevel, 2),
                distance = Math.Round(totalDistance, 2),
                timestamp = DateTime.UtcNow
            };

            var messageString = JsonConvert.SerializeObject(telemetry);
            var message = new Message(Encoding.UTF8.GetBytes(messageString));
            message.Properties.Add("speedAlert", currentSpeed > MaxSpeed * 0.9 ? "true" : "false");
            message.Properties.Add("fuelAlert", fuelLevel < 20 ? "true" : "false");

            await _moduleClient!.SendEventAsync("output1", message, _cancellationToken);
            _logger.LogInformation("Telemetry sent: {message}", messageString);

            await Task.Delay(telemetryInterval * 1000, _cancellationToken);
        }
    }

    private void AdjustSpeed()
    {
        double targetSpeed = MinSpeed + (MaxSpeed - MinSpeed) * random.NextDouble();

        if (currentSpeed < targetSpeed)
        {
            currentAcceleration = 5 + 5 * random.NextDouble();
            currentSpeed = Math.Min(targetSpeed, currentSpeed + currentAcceleration * telemetryInterval);
        }
        else
        {
            currentAcceleration = -4 - 4 * random.NextDouble();
            currentSpeed = Math.Max(targetSpeed, currentSpeed + currentAcceleration * telemetryInterval);
        }

        totalDistance += (currentSpeed * telemetryInterval) / 3600;
        fuelLevel -= (0.05 + 0.2 * (currentSpeed / MaxSpeed)) * telemetryInterval;
        fuelLevel = Math.Max(0, fuelLevel);

        if (fuelLevel <= 0)
        {
            fuelLevel = 100;
            _logger.LogInformation("Fuel tank refilled.");
        }
    }

    private Task<MethodResponse> SetTelemetryIntervalHandler(MethodRequest request, object userContext)
    {
        var data = Encoding.UTF8.GetString(request.Data);
        if (int.TryParse(data, out int interval) && interval > 0)
        {
            telemetryInterval = interval;
            _logger.LogInformation("Telemetry interval set to {interval}s", interval);
            return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes("{\"result\":\"OK\"}"), 200));
        }
        return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes("{\"result\":\"Invalid interval\"}"), 400));
    }
}
