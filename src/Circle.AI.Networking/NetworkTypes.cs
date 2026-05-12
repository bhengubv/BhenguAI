namespace Circle.AI.Networking;

public enum TransportKind
{
    Http,
    WebSocket,
    Grpc,
    Mqtt,
    Tcp,
    Udp,
    WiFi,        // WiFi Direct / mDNS / LAN — no Aether required
    Bluetooth,   // raw BLE GATT — no Aether required
    NearLink,    // Huawei SLE / HarmonyOS — no Aether required
    Aether,      // full Aether mesh (Signal E2E + AODV + SOS)
    Dtn,         // 72hr store-and-forward over any transport
    LocalStore   // offline queue — no live path at all
}

public enum ConnectivityState { Online, LocalOnly, MeshOnly, Offline }

public enum MessagePriority { Low, Normal, High, Urgent, Emergency }

public enum SyncDeliveryMode { BestEffort, Guaranteed, Urgent }

public enum PeerRole { Peer, Relay, Bridge, Sink }
