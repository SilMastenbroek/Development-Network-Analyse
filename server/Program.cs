using System;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MessageNS;


// Do not modify this class
class Program
{
    static void Main(string[] args)
    {
        ServerUDP sUDP = new ServerUDP();
        sUDP.start(); // Start de server
    }
}

class ServerUDP
{
    // De socket waarmee de UDP-berichten verzenden en ontvangen
    private Socket serverSocket;

    // Het IP + poort van de client die we bedienen
    private EndPoint remoteEndpoint;

    // Server luistert op poort 11000
    private const int port = 11000;

    // Hoofdloop van server
    public void start()
    {
        System.Console.WriteLine("Server word opgestart...");
        InitializeSocket(); // Socket opzetten en binden
        System.Console.WriteLine("Server luistert op poort " + port);

        while (true)
        {
            try
            {
                // Ontvang binnendkomend bericht
                Message message = ReceiveMessage();

                // Genegeerd (bijvoorbeeld een eigen bericht)
                if (message.MsgId == -1)
                    continue;

                // Verwerk op basis van MessageType
                if (message.Type == MessageType.Hello)
                    HandleHello(message);

                else if(message.Type == MessageType.DNSLookup)
                    HandleDNSLookup(message);

                else if (message.Type == MessageType.Ack)
                    HandleAck(message);

                else
                    System.Console.WriteLine("Verwachtte Hello, maar kreeg iets anders: " + message.Type);       
            }
            catch (System.Exception e)
            {
                System.Console.WriteLine("Fout tijdens verwerking: " + e.Message);
            }
        }
    }

    private void InitializeSocket()
    {
        // Setup: nieuwe UDP socket, bind op poort 11000
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
        serverSocket.Bind(localEndPoint);

        // Placeholder voor remote client
        remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
    }

    private Message ReceiveMessage()
    {
        byte[] buffer = new byte[4096];
        int received = serverSocket.ReceiveFrom(buffer, ref remoteEndpoint);

        // Filter: voorkom dat de server zijn eigen bericht opvangt
        if (remoteEndpoint is IPEndPoint remoteIp &&
            remoteIp.Address.Equals(IPAddress.Loopback) &&
            remoteIp.Port == ((IPEndPoint)serverSocket.LocalEndPoint!).Port)
        {
            Console.WriteLine("Eigen bericht genegeerd.");
            return new Message { MsgId = -1, Type = MessageType.Error, Content = "Echo ignored" };
        }

        // Verwerk binnengekomen bericht
        string jsonMessage = Encoding.UTF8.GetString(buffer, 0, received);
        Console.WriteLine("Bericht:\n" + jsonMessage);

        Message? message = JsonSerializer.Deserialize<Message>(jsonMessage);
        return message ?? throw new Exception("Kon bericht niet parsen.");
    }

    private void HandleHello(Message message)
    {
        System.Console.WriteLine("Hello ontvangen met MsgId " + message.MsgId);

        // Stuur een Welcome met een nieuwe ID terug
        SendWelcome(message.MsgId);
    }

    private void SendWelcome(int replyMsgId)
    {
        // Maak Welcome-message
        Message welcome = new Message
        {
            MsgId = replyMsgId,
            Type = MessageType.Welcome,
            Content = "Welkom van de server!"
        };

        // Zet om naar JSON en verstuur naar de client
        string json = JsonSerializer.Serialize(welcome);
        byte[] data = Encoding.UTF8.GetBytes(json);
        serverSocket.SendTo(data, remoteEndpoint);

        System.Console.WriteLine("Welkom verzonden!");
    }

    private void HandleDNSLookup(Message message)
    {
        System.Console.WriteLine("DNSLookup ontvangen met MsgId " + message.MsgId);

        try
        {
            // Parse content als DNSRecord (Type + Name)
            var lookupRequest = JsonSerializer.Deserialize<DNSRecord>(message.Content.ToString() ?? "");

            // Validate: incomplete lookup?
            if (lookupRequest == null || string.IsNullOrEmpty(lookupRequest.Type) || string.IsNullOrEmpty(lookupRequest.Name))
            {
                SendError(message.MsgId, "Ongeldige lookup content.");
                return;
            }

            // Laad DNS-records uit JSON bestand
            string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "dns_records.json");
            if (!File.Exists(jsonPath))
            {
                SendError(message.MsgId, "DNS bestand niet gevonden");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            List<DNSRecord>? records = JsonSerializer.Deserialize<List<DNSRecord>>(json);

            if (records == null)
            {
                SendError(message.MsgId, "Kan DNS-bestand niet inlezen.");
                return;
            }

            // Zoek naar een match (Type en Name)
            var match = records.FirstOrDefault(r =>
                r.Type.Equals(lookupRequest.Type, StringComparison.OrdinalIgnoreCase) &&
                r.Name.Equals(lookupRequest.Name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                // Record gevonden: stuur DNSLookupReply
                Message reply = new Message
                {
                    MsgId = message.MsgId,
                    Type = MessageType.DNSLookupReply,
                    Content = JsonSerializer.Serialize(match)
                };

                string replyJson = JsonSerializer.Serialize(reply);
                byte[] replyBytes = Encoding.UTF8.GetBytes(replyJson);
                serverSocket.SendTo(replyBytes, remoteEndpoint);
                Console.WriteLine("DNSLookupReply verzonden.");
            }
            else
            {
                // Geen match
                SendError(message.MsgId, "Geen DNS-record gevonden voor " + lookupRequest.Name);
            }
        }
        catch (System.Exception e)
        {
            SendError(message.MsgId, "Fout tijdens verwerken van lookup: " + e.Message);
        }
    }

    public void SendError(int originalMsgId, string errorMessage)
    {
        Message error = new Message
        {
            MsgId = originalMsgId,
            Type = MessageType.Error,
            Content = errorMessage
        };

        string errorJson = JsonSerializer.Serialize(error);
        byte[] errorBytes = Encoding.UTF8.GetBytes(errorJson);
        serverSocket.SendTo(errorBytes, remoteEndpoint);
        System.Console.WriteLine("Error verzenden:" + errorMessage);
    }

    public void HandleAck(Message message)
    {
        System.Console.WriteLine("Ack ontvangen voor MsgId: " + message.Content);
        SendEnd();
    }

    private void SendEnd()
    {
        Message endMessage = new Message
        {
            MsgId = 9999,
            Type = MessageType.End,
            Content = "Alle lookups afgehandeld"
        };

        string json = JsonSerializer.Serialize(endMessage);
        byte[] data = Encoding.UTF8.GetBytes(json);
        serverSocket.SendTo(data, remoteEndpoint);

        System.Console.WriteLine("End verzonden naar client");
    }
}