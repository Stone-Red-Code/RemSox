namespace RemSox.Shared.Networking;

public sealed class TcpMessage
{
    public string Type { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = [];
}
