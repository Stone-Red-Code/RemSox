using RemSox.Shared.Networking;

namespace RemSox.Viewer;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("RemSox RDP Viewer");
        Console.WriteLine("=================");

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: RemSox.Viewer <host> <port>");
            return;
        }

        string host = args[0];
        int port = int.Parse(args[1]);

        TcpRpcClient client = new();
        client.ListenTo("RenderCmd", async (payload) =>
        {
            Console.WriteLine($"Received render command: {payload.Length} bytes");
        });

        try
        {
            client.Connect(host, port);
            Console.WriteLine($"Connected to {host}:{port}");

            await client.RequestAsync("SyncRequest", []);
            Console.WriteLine("Sync request sent.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        Console.WriteLine("Press Ctrl+C to exit.");
        await Task.Delay(Timeout.Infinite);
    }
}
