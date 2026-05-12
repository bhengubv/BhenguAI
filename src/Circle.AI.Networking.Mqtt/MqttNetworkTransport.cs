using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Client;
using Circle.AI.Networking;

namespace Circle.AI.Networking.Mqtt;

/// <summary>
/// <see cref="INetworkTransport"/> backed by an MQTT broker.
/// Publishes to <c>circle/payloads/{destinationId}</c> and subscribes to
/// <c>circle/payloads/{localClientId}/#</c>.
/// </summary>
public sealed class MqttNetworkTransport : INetworkTransport, IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly string _localClientId;
    private readonly Channel<NetworkPayload> _inbound =
        Channel.CreateUnbounded<NetworkPayload>();

    public TransportKind Kind    => TransportKind.Mqtt;
    public bool IsAvailable      => _client.IsConnected;

    public MqttNetworkTransport(string brokerHost, int port, string clientId,
        string? username = null, string? password = null)
    {
        _localClientId = clientId;
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, port)
            .WithClientId(clientId)
            .WithCleanSession(false);

        if (username is not null)
            builder = builder.WithCredentials(username, password);

        _options = builder.Build();

        _client.ApplicationMessageReceivedAsync += OnMessageReceived;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _client.ConnectAsync(_options, ct).ConfigureAwait(false);
        await _client.SubscribeAsync($"circle/payloads/{_localClientId}/#", cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _client.DisconnectAsync(cancellationToken: ct).ConfigureAwait(false);
        _inbound.Writer.TryComplete();
    }

    public async Task SendAsync(NetworkPayload payload, CancellationToken ct = default)
    {
        var topic = payload.DestinationId is { Length: > 0 } d
            ? $"circle/payloads/{d}"
            : "circle/payloads/broadcast";

        var msg = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload.Data.ToArray())
            .WithQualityOfServiceLevel(payload.Priority >= MessagePriority.High
                ? MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce
                : MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.PublishAsync(msg, ct).ConfigureAwait(false);
    }

    public IAsyncEnumerable<NetworkPayload> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
        => _inbound.Reader.ReadAllAsync(ct);

    private Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        var p = NetworkPayload.Create(e.ApplicationMessage.PayloadSegment.ToArray());
        _inbound.Writer.TryWrite(p);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.ApplicationMessageReceivedAsync -= OnMessageReceived;
        await _client.DisconnectAsync().ConfigureAwait(false);
        _client.Dispose();
    }
}
