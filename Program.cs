
using System.Net;
using System.Net.Sockets;

public class Program
{
    private static async Task Main(string[] args)
    {
        var response = await PortForwardUtil.Setup(new IPAddress(new byte[] { 192, 168, 1, 1 }), (ushort)65522, (ushort)52001, TimeSpan.FromHours(1));

        if (response.Lifetime == TimeSpan.Zero)
        {
            if (response.PrivatePort == 0)
            {
                Console.WriteLine("All mappings are cleared.");
            }
            else
            {
                Console.WriteLine($"Mapping to private port {response.PrivatePort} has been cleared.");
            }
        }
        else
        {
            Console.WriteLine($"Port {response.PublicPort} has been mapped to private port {response.PrivatePort} for {response.Lifetime}.");
        }
    }
}

public static class PortForwardUtil
{
    public static Task<PortMappingResponse> Setup(IPAddress defaultGateway, ushort privatePort, ushort publicPort, TimeSpan lifetime)
    {
        return SetupPortMapping(privatePort, publicPort, lifetime, defaultGateway);
    }

    public static Task<PortMappingResponse> Clear(IPAddress defaultGateway, ushort? privatePort = 0)
    {
        return SetupPortMapping(privatePort ?? 0, 0, TimeSpan.Zero, defaultGateway);
    }

    /// <summary>
    /// Setups a Port Mapping following NAT Port Mapping Protocol.
    /// </summary>
    private static async Task<PortMappingResponse> SetupPortMapping(ushort privatePort, ushort publicPort, TimeSpan lifetime, IPAddress defaultGateway)
    {
        var privatePortBytes = BitConverter.GetBytes((ushort)privatePort);
        var publicPortBytes = BitConverter.GetBytes((ushort)publicPort);
        var lifetimeBytes = BitConverter.GetBytes((uint)lifetime.TotalSeconds);

        // Construct a UDP datagram packet following http://miniupnp.free.fr/nat-pmp.html
        var packet = new byte[] {
            0,                      // Version
            1,                      // UDP
            0,                      // Reserved
            0,                      // Reserved
            privatePortBytes[1],    // Private port (higher byte first)
            privatePortBytes[0],    // Private port
            publicPortBytes[1],     // Public port (higher byte first)
            publicPortBytes[0],     // Public port
            lifetimeBytes[3],       // Lifetime in seconds (higher byte first)
            lifetimeBytes[2],       // Lifetime in seconds
            lifetimeBytes[1],       // Lifetime in seconds
            lifetimeBytes[0]        // Lifetime in seconds
        };

        var gateway = new IPEndPoint(defaultGateway, 5351);
        using (var client = new UdpClient())
        {
            client.Connect(gateway);

            var sent = await client.SendAsync(packet, packet.Length);
            if (sent != packet.Length)
            {
                throw new ApplicationException("Not all bytes are sent to register for a port mapping.");
            }

            var udpResults = await client.ReceiveAsync();
            return new PortMappingResponse(udpResults.Buffer);
        }
    }
}

public class PortMappingResponse
{
    public PortMappingResponse(byte[] bytes)
    {
        IsUdp = bytes[1] == 129 ? true : false;
        Array.Reverse<byte>(bytes, 2, 2);
        ResultCode = (ResultCode)BitConverter.ToUInt16(bytes, 2);
        Array.Reverse<byte>(bytes, 4, 4);
        TimeSinceMappingTableInit = TimeSpan.FromSeconds(BitConverter.ToUInt32(bytes, 4));
        Array.Reverse<byte>(bytes, 8, 2);
        PrivatePort = BitConverter.ToUInt16(bytes, 8);
        Array.Reverse<byte>(bytes, 10, 2);
        PublicPort = BitConverter.ToUInt16(bytes, 10);
        Array.Reverse<byte>(bytes, 12, 4);
        Lifetime = TimeSpan.FromSeconds(BitConverter.ToUInt32(bytes, 12));
    }

    public bool IsUdp { get; }
    public ResultCode ResultCode { get; }
    public TimeSpan TimeSinceMappingTableInit { get; }
    public ushort PrivatePort { get; }
    public ushort PublicPort { get; }
    public TimeSpan Lifetime { get; }
}

public enum ResultCode : ushort
{
    Success = 0,
    UnsupportedVersion = 1,
    NotAuthorizedOrRefused = 2,
    NetworkFailure = 3,
    OutOfResource = 4,
    UnsupportedOpCode = 5
}