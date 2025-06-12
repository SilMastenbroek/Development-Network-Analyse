using System.Collections.Immutable;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LibData;

// SendTo();
class Program
{
    static void Main(string[] args)
    {
        ClientUDP.start();
    }
}


public class Setting
{
    public int ServerPortNumber { get; set; }
    public string? ServerIPAddress { get; set; }
}


class ClientUDP
{
    //TODO: [Deserialize Setting.json]
    // ✅ Lees de configuratie uit het JSON-bestand en zet om naar Setting-object
    static Setting? setting = JsonSerializer.Deserialize<Setting>(File.ReadAllText(@"settings.json"));

    public static void start()
    {
        while (true)
        {
            Console.WriteLine("\n📋 Kies een testactie:");
            Console.WriteLine("1. Start sessie en verstuur DNSLookups");
            Console.WriteLine("0. Stop de client");
            Console.Write("➤ ");
            string? input = Console.ReadLine();

            if (input == "0") break;
            if (input == "1") RunClientSession();
            else Console.WriteLine("❗ Ongeldige keuze. Probeer opnieuw.");
        }
    }


    static void RunClientSession()
    {
        //TODO: [Create endpoints and socket]
        // ✅ Alleen het serverendpoint is nodig, client bindt niet aan specifieke IP/poort
        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(setting.ServerIPAddress!), setting.ServerPortNumber);
        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        Console.WriteLine($"🔌 Client socket created. Sending to server {serverEndPoint}");
        // 🔌 Geen expliciete binding, deze regel is verwijderd omdat clientEndPoint niet meer bestaat

        int msgId = new Random().Next(10000, 99999);
        //TODO: [Create and send HELLO]
        // ✅ Stuur Hello-bericht naar server bij het opstarten van de sessie
        Message hello = new Message { MsgId = msgId++, MsgType = MessageType.Hello, Content = "Hello from client" };
        SendMessage(clientSocket, hello, serverEndPoint);

        //TODO: [Receive and print Welcome from server]
        // ✅ Ontvang Welcome van de server en controleer het antwoord
        Message? welcomeMsg = ReceiveMessage(clientSocket);
        if (welcomeMsg == null || welcomeMsg.MsgType != MessageType.Welcome)
        {
            Console.WriteLine("❌ Geen geldige Welcome ontvangen. Sessie beëindigd.");
            //TODO: [Close the client socket]
            // ✅ Sluit de verbinding aan het einde van de sessie
            clientSocket.Close();
            return;
        }

        Console.WriteLine($"📥 Ontvangen Welcome: {welcomeMsg.Content}\n");

        //TODO: [Create and send DNSLookup Message]
        // ✅ Maak lijst met test-DNSLookups (correct en fout)
        var lookups = new List<DNSRecord>
        {
            new DNSRecord { Type = "A", Name = "www.example.com" },
            new DNSRecord { Type = "MX", Name = "mail.example.com" },
            new DNSRecord { Type = "A", Name = "invalid" },
            new DNSRecord { Type = "A", Name = "notfound.domain" }
        };

        while (true)
        {
            Console.WriteLine("\n🌐 Kies een DNS-lookup:");
            for (int i = 0; i < lookups.Count; i++)
                Console.WriteLine($"{i + 1}. Type: {lookups[i].Type}, Name: {lookups[i].Name}");

            Console.WriteLine("0. Stop sessie");
            Console.Write("➤ Keuze: ");
            string? input = Console.ReadLine();

            if (input == "0") break;
            if (!int.TryParse(input, out int index) || index < 1 || index > lookups.Count)
            {
                Console.WriteLine("❗ Ongeldige invoer");
                continue;
            }

            var record = lookups[index - 1];
            //TODO: [Send DNSLookup to server]
            // ✅ Stuur gekozen DNSLookup naar server
            Message lookupMsg = new Message { MsgId = msgId++, MsgType = MessageType.DNSLookup, Content = record };
            SendMessage(clientSocket, lookupMsg, serverEndPoint);

            //TODO: [Receive and print DNSLookupReply from server]
            // ✅ Ontvang antwoord van server (Reply, Error of End)
            Message? reply = ReceiveMessage(clientSocket);
            if (reply == null) continue;

            if (reply.MsgType == MessageType.End)
            {
                Console.WriteLine($"⛔ Server stuurde End: {reply.Content}");
                break;
            }
            else if (reply.MsgType == MessageType.Error)
            {
                Console.WriteLine($"❌ Fout: {reply.Content}");
            }
            else if (reply.MsgType == MessageType.DNSLookupReply)
            {
                Console.WriteLine($"✅ Antwoord: {JsonSerializer.Serialize(reply.Content)}");
                Console.Write("↪️  Stuur ACK? (j/n): ");
                string? ackChoice = Console.ReadLine();

                if (ackChoice?.Trim().ToLower() == "j")
                {
                    //TODO: [Send Acknowledgment to Server]
                    // ✅ Verstuur ACK naar server voor de oorspronkelijke MsgId
                    Message ack = new Message { MsgId = msgId++, MsgType = MessageType.Ack, Content = lookupMsg.MsgId };
                    SendMessage(clientSocket, ack, serverEndPoint);
                }
                else Console.WriteLine("⚠️  ACK niet verstuurd");
            }
        }

        clientSocket.Close();
        Console.WriteLine("🔒 Sessie beëindigd en verbinding gesloten.");
    }

    static void SendMessage(Socket socket, Message msg, EndPoint target)
    {
        string json = JsonSerializer.Serialize(msg);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        socket.SendTo(bytes, target);
        Console.WriteLine($"📤 Sent {msg.MsgType} (MsgId: {msg.MsgId})");
    }

    static Message? ReceiveMessage(Socket socket)
    {
        byte[] buffer = new byte[4096];
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            int received = socket.ReceiveFrom(buffer, ref remoteEP);
            string json = Encoding.UTF8.GetString(buffer, 0, received);
            Message? msg = JsonSerializer.Deserialize<Message>(json);
            Console.WriteLine($"📥 Received {msg?.MsgType} (MsgId: {msg?.MsgId})");
            return msg;
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"❌ Fout bij ontvangen: {ex.Message}");
            return null;
        }
    }
}
