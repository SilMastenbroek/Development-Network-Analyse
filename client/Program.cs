using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MessageNS;

class Program
{
    static void Main(string[] args)
    {
        ClientUDP cUDP = new ClientUDP();
        cUDP.start(); // Start de clientlogica
    }
}

class ClientUDP
{
    public void start()
    {
        // Server info
        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Loopback, 11000);
        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Voor ontvangst
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[4096];

        // Stap 1 – Stuur Hello
        Message helloMessage = new Message
        {
            MsgId = 1,
            Type = MessageType.Hello,
            Content = "Hello vanaf de client!"
        };

        string helloJson = JsonSerializer.Serialize(helloMessage);
        byte[] helloBytes = Encoding.UTF8.GetBytes(helloJson);
        clientSocket.SendTo(helloBytes, serverEndPoint);
        Console.WriteLine("📤 Hello verzonden!");

        // Stap 2 – Ontvang Welcome
        int received = clientSocket.ReceiveFrom(buffer, ref remote);
        string response = Encoding.UTF8.GetString(buffer, 0, received);
        Console.WriteLine("📩 Antwoord (Welcome):\n" + response);

        // Stap 3 – Meerdere DNSLookups (2 goed, 2 fout)
        List<DNSRecord> lookups = new List<DNSRecord>
        {
            new DNSRecord { Type = "A", Name = "www.example.com" },             // goed
            new DNSRecord { Type = "MX", Name = "mail.example.com" },           // goed
            new DNSRecord { Type = "A", Name = "niet-bestaand.nl" },            // fout
            new DNSRecord { Type = "CNAME", Name = "invalid.example.com" }      // fout
        };

        int msgId = 33;
        foreach (var record in lookups)
        {
            // 1. Stuur DNSLookup
            Message lookupMessage = new Message
            {
                MsgId = msgId,
                Type = MessageType.DNSLookup,
                Content = JsonSerializer.Serialize(record)
            };

            string lookupJson = JsonSerializer.Serialize(lookupMessage);
            byte[] lookupBytes = Encoding.UTF8.GetBytes(lookupJson);
            clientSocket.SendTo(lookupBytes, serverEndPoint);
            Console.WriteLine($"📤 DNSLookup verzonden: {record.Type} {record.Name}");

            // 2. Ontvang antwoord
            received = clientSocket.ReceiveFrom(buffer, ref remote);
            string reply = Encoding.UTF8.GetString(buffer, 0, received);
            Console.WriteLine($"📩 Antwoord op lookup:\n{reply}");

            // 3. Stuur Ack terug
            Message ack = new Message
            {
                MsgId = msgId + 1000,
                Type = MessageType.Ack,
                Content = msgId.ToString()
            };

            string ackJson = JsonSerializer.Serialize(ack);
            byte[] ackBytes = Encoding.UTF8.GetBytes(ackJson);
            clientSocket.SendTo(ackBytes, serverEndPoint);
            Console.WriteLine($"📤 Ack verzonden voor MsgId: {msgId}");

            msgId += 1;
        }

        // Stap 4 – Wacht op End van server
        int endReceived = clientSocket.ReceiveFrom(buffer, ref remote);
        string endMsg = Encoding.UTF8.GetString(buffer, 0, endReceived);
        Message? endMessage = JsonSerializer.Deserialize<Message>(endMsg);

        if (endMessage != null && endMessage.Type == MessageType.End)
        {
            Console.WriteLine("📩 End ontvangen van server. Client wordt afgesloten.");
        }
        else
        {
            Console.WriteLine("⚠️ Onverwacht bericht na lookups:");
            Console.WriteLine(endMsg);
        }

        // Sluit socket netjes af
        clientSocket.Close();
    }
}
