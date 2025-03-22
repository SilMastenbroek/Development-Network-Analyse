using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using MessageNS;

class Program
{
    static void Main(string[] args)
    {
        var testLookups = new List<DNSRecord>
        {
            new DNSRecord { Type = "A", Name = "www.example.com" },       // geldig
            new DNSRecord { Type = "MX", Name = "mail.example.com" },      // geldig
            new DNSRecord { Type = "A", Name = "nonexistent.domain" },     // ongeldig
            new DNSRecord { Type = "CNAME", Name = "alias.example.com" }   // ongeldig of niet ondersteund
        };

        foreach (var dnsLookup in testLookups)
        {
            var client = new ClientUDP();
            client.start(dnsLookup);
        }
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
    private const int ServerPort = 11000;
    private readonly Socket _socket;
    private readonly IPEndPoint _serverEndpoint;
    private int MsgIdCount = 0;

    public ClientUDP()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _serverEndpoint = new IPEndPoint(IPAddress.Loopback, ServerPort);
    }

    public void start(DNSRecord dnsLookup)
    {
        try
        {
            Console.WriteLine("\n\n\nClient: Starting client met DNSlookup: \n");

            Message helloMsg = new Message
            {
                MsgId = ++MsgIdCount,
                Type = MessageType.Hello,
                Content = "Hello from client"
            };
            SendMessage(helloMsg);

            var welcome = ReceiveMessage();
            if (welcome.Type != MessageType.Welcome)
            {
                Console.WriteLine("Received: Did not receive Welcome message from server. Exiting.");
                return;
            }
            if (welcome.MsgId != MsgIdCount)
            {
                Console.WriteLine("Received: Wrong message ID. Expected: " + MsgIdCount + " got: " + welcome.MsgId);
                return;
            }
            Console.WriteLine("Client: Handshake completed. Connected to server.");

            Message dnsLookupMsg = new Message
            {
                MsgId = ++MsgIdCount,
                Type = MessageType.DNSLookup,
                Content = JsonSerializer.Serialize(dnsLookup)
            };
            SendMessage(dnsLookupMsg);
            int dnsLookupReplyMsgId = dnsLookupMsg.MsgId;

            Message DnsLookupReply = ReceiveMessage();
            if (DnsLookupReply.Type != MessageType.DNSLookupReply)
            {
                Console.WriteLine("Received: Expected DNSLookupReply message but got: " + DnsLookupReply.Type);
                return;
            }
            if (DnsLookupReply.MsgId != dnsLookupReplyMsgId)
            {
                Console.WriteLine("Received: Wrong DNSLookupReply message ID. Expected: " + dnsLookupReplyMsgId + " got: " + DnsLookupReply.MsgId);
                return;
            }

            Message ackMsg = new Message
            {
                MsgId = ++MsgIdCount,
                Type = MessageType.Ack,
                Content = dnsLookupReplyMsgId.ToString()
            };
            SendMessage(ackMsg);

            Message endMsg = ReceiveMessage();
            if (endMsg.Type != MessageType.End)
            {
                Console.WriteLine("Received: Expected End message but got: " + endMsg.Type);
                return;
            }
            Console.WriteLine("End ontvangen. Exiting client.\n\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client: Error: {ex.Message}");
        }
    }

    private void SendMessage(Message msg)
    {
        string json = JsonSerializer.Serialize(msg);
        byte[] data = Encoding.UTF8.GetBytes(json);
        _socket.SendTo(data, _serverEndpoint);
        // Log DNSRecord apart indien van toepassing
        LogMessage(msg, "Sent");
    }

    private Message ReceiveMessage()
    {
        byte[] buffer = new byte[4096];
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        int received = _socket.ReceiveFrom(buffer, ref remoteEP);
        var parsed = JsonSerializer.Deserialize<Message>(Encoding.UTF8.GetString(buffer, 0, received))!;
        LogMessage(parsed, "Received");
        return parsed;
    }

    private void LogMessage(Message msg, string prefix = "Sent")
    {
            Console.WriteLine($"{prefix}: " + JsonSerializer.Serialize(msg, new JsonSerializerOptions { WriteIndented = true }));
    }
}
