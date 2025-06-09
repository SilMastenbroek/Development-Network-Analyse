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
    public int ClientPortNumber { get; set; }
    public string? ClientIPAddress { get; set; }
}

class ClientUDP
{

    //TODO: [Deserialize Setting.json]
    static string configFile = @"../Setting.json";
    static string configContent = File.ReadAllText(configFile);
    static Setting? setting = JsonSerializer.Deserialize<Setting>(configContent);


    public static void start()
    {

        //TODO: [Create endpoints and socket]
        // ✅ Maak de client- en server-endpoints aan op basis van instellingen uit Setting.json
        IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Parse(setting.ClientIPAddress!), setting.ClientPortNumber);
        IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(setting.ServerIPAddress!), setting.ServerPortNumber);

        // ✅ Maak een UDP-socket en bind deze aan het clientadres
        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        clientSocket.Bind(clientEndPoint);

        Console.WriteLine($"🔌 Client socket created and bound to {clientEndPoint}");


        //TODO: [Create and send HELLO]
        // ✅ Maak Hello-bericht en stuur het naar de server
        int msgId = 1;
        Message hello = new Message { MsgId = msgId++, MsgType = MessageType.Hello, Content = "Hello from client" };
        string helloJson = JsonSerializer.Serialize(hello);
        byte[] helloBytes = Encoding.UTF8.GetBytes(helloJson);
        clientSocket.SendTo(helloBytes, serverEndPoint);
        Console.WriteLine($"📤 Sent Hello: {helloJson}");

        //TODO: [Receive and print Welcome from server]
        // ✅ Wacht op Welcome-bericht van de server
        byte[] buffer = new byte[4096];
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        int bytesReceived = clientSocket.ReceiveFrom(buffer, ref remoteEP);
        string receivedJson = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
        Message? welcomeMsg = JsonSerializer.Deserialize<Message>(receivedJson);
        Console.WriteLine($"📥 Received from server: {receivedJson}");

        if (welcomeMsg == null || welcomeMsg.MsgType != MessageType.Welcome)
        {
            Console.WriteLine("❌ Unexpected response from server. Exiting.");
            return;
        }

        // TODO: [Create and send DNSLookup Message]
        // ✅ Maak een lijst met testopvragingen (2 correct, 2 fout)
        var lookups = new List<DNSRecord>
        {
            new DNSRecord { Type = "A", Name = "www.example.com" },
            new DNSRecord { Type = "MX", Name = "mail.example.com" },
            new DNSRecord { Type = "A", Name = "invalid" },
            new DNSRecord { Type = "A", Name = "notfound.domain" }
        };


        //TODO: [Receive and print DNSLookupReply from server]
        foreach (var record in lookups)
        {
            Message lookupMsg = new Message { MsgId = msgId, MsgType = MessageType.DNSLookup, Content = record };
            string lookupJson = JsonSerializer.Serialize(lookupMsg);
            byte[] lookupBytes = Encoding.UTF8.GetBytes(lookupJson);
            clientSocket.SendTo(lookupBytes, serverEndPoint);
            Console.WriteLine($"📤 Sent DNSLookup (MsgId {msgId}): {lookupJson}");

            // Wacht op reactie
            buffer = new byte[4096];
            remoteEP = new IPEndPoint(IPAddress.Any, 0);
            bytesReceived = clientSocket.ReceiveFrom(buffer, ref remoteEP);
            string replyJson = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
            Message? reply = JsonSerializer.Deserialize<Message>(replyJson);
            Console.WriteLine($"📥 Received: {replyJson}");


            //TODO: [Send Acknowledgment to Server]
            if (reply != null && reply.MsgType == MessageType.DNSLookupReply)
            {
                Message ack = new Message { MsgId = msgId++, MsgType = MessageType.Ack, Content = lookupMsg.MsgId };
                string ackJson = JsonSerializer.Serialize(ack);
                byte[] ackBytes = Encoding.UTF8.GetBytes(ackJson);
                clientSocket.SendTo(ackBytes, serverEndPoint);
                Console.WriteLine($"📤 Sent ACK for MsgId {lookupMsg.MsgId}");
            }
            else if (reply != null && reply.MsgType == MessageType.Error)
            {
                Console.WriteLine($"❌ Server responded with error: {reply.Content}");
            }
        }

        // TODO: [Send next DNSLookup to server]
        // ✅ Wordt automatisch verwerkt in de foreach-loop hierboven waarin we alle DNSRecord-verzoeken één voor één versturen

        //TODO: [Receive and print End from server]
        // ✅ Wacht op End-bericht na laatste Ack
        buffer = new byte[4096];
        remoteEP = new IPEndPoint(IPAddress.Any, 0);
        bytesReceived = clientSocket.ReceiveFrom(buffer, ref remoteEP);
        string endJson = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
        Message? endMsg = JsonSerializer.Deserialize<Message>(endJson);
        if (endMsg != null && endMsg.MsgType == MessageType.End)
        {
            Console.WriteLine("✅ Received End message from server. Session complete.");
        }

    }
}
