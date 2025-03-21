using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using MessageNS;

// Data model for DNS records
public class DNSRecord
{
    public string Type { get; set; }
    public string Name { get; set; }
    public string? Value { get; set; }
    public int? TTL { get; set; }
    public int? Priority { get; set; }
}

public class DNSClient
{
    private const int ServerPort = 9000;
    private readonly Socket _socket;
    private readonly IPEndPoint _serverEndpoint;

    public DNSClient(string serverIp)
    {
        // Stap 1: socket aanmaken en server IP/poort instellen
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _serverEndpoint = new IPEndPoint(IPAddress.Parse(serverIp), ServerPort);
    }

    public void Run()
    {
        try
        {
            // Stap 2: verstuur Hello bericht naar de server
            var helloMsg = new Message { Type = MessageType.Hello, Content = "Hello from client" };
            Console.WriteLine(helloMsg);
            SendMessage(helloMsg);

            // Stap 3: wacht op Welcome bericht van server
            var welcome = ReceiveMessage();
            Console.WriteLine(welcome);
            if (welcome.Type != MessageType.Welcome)
            {
                Console.WriteLine("Did not receive Welcome message from server. Exiting.");
                return;
            }

            Console.WriteLine("Handshake completed. Connected to server.\n");

            // Stap 4: begin menu-loop voor DNS lookups
            while (true)
            {
                Console.WriteLine("--- DNS Client Menu ---");
                Console.WriteLine("1. Send DNS Lookup");
                Console.WriteLine("2. Exit");
                Console.Write("Choose option: ");
                var choice = Console.ReadLine();

                if (choice == "1")
                {
                    // Stap 5: vraag gebruiker om type en naam
                    Console.Write("Type (A or MX): ");
                    string type = Console.ReadLine() ?? "";
                    Console.Write("Name (e.g., www.example.com): ");
                    string name = Console.ReadLine() ?? "";

                    // Stap 6: maak DNSRequest aan en verstuur naar server
                    var request = new DNSRecord { Type = type, Name = name };
                    var dnsLookupMsg = new Message
                    {
                        Type = MessageType.RequestData,
                        Content = JsonSerializer.Serialize(request)
                    };
                    Console.WriteLine(dnsLookupMsg);
                    SendMessage(dnsLookupMsg);

                    // Stap 7: ontvang antwoord van server (Data of Error)
                    var reply = ReceiveMessage();
                    Console.WriteLine(reply);
                    Console.WriteLine($"[Client] Received: {reply.Type} | Content length: {reply.Content?.Length} chars");

                    // Stap 8: bevestig ontvangst met Ack
                    var ackMsg = new Message { Type = MessageType.Ack, Content = name };
                    Console.WriteLine(ackMsg);
                    SendMessage(ackMsg);
                }
                else if (choice == "2")
                {
                    // Stap 9: stuur End bericht naar server en stop client
                    var endMsg = new Message { Type = MessageType.End, Content = "End from client" };
                    Console.WriteLine(endMsg);
                    Console.WriteLine("Exiting client.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Client] Error: {ex.Message}");
        }
    }

    // Verstuur bericht via UDP socket
    private void SendMessage(Message msg)
    {
        string json = JsonSerializer.Serialize(msg);
        byte[] data = Encoding.UTF8.GetBytes(json);
        _socket.SendTo(data, _serverEndpoint);
        Console.WriteLine($"[Client] Sent {data.Length} bytes to server.");
    }

    // Ontvang bericht via UDP socket
    private Message ReceiveMessage()
    {
        byte[] buffer = new byte[4096];
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        int size = _socket.ReceiveFrom(buffer, ref remoteEP);
        Console.WriteLine($"[Client] Received {size} bytes from server.");
        string json = Encoding.UTF8.GetString(buffer, 0, size);
        return JsonSerializer.Deserialize<Message>(json)!;
    }


}

// Server code exists in separate solution
public class Program
{
    public static void Main(string[] args)
    {
        // Default: always start client with predefined server IP (e.g. localhost)
        string serverIp = args.Length > 0 ? args[0] : "127.0.0.1";

        // Start de DNS client
        new DNSClient(serverIp).Run();
    }
}
