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
        IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(setting.ServerIPAddress!), setting.ServerPortNumber);
        Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        //TODO: [Create and send HELLO]
        Message helloMessage = new Message { MsgId = 1, MsgType = MessageType.Hello, Content = "Hello from client" };
        SendMessage(clientSocket, serverEndpoint, helloMessage);

        //TODO: [Receive and print Welcome from server]
        ReceiveMessage(clientSocket);

        DNSRecord[] lookups = new DNSRecord[] {
            new DNSRecord { Type = "A", Name = "www.outlook.com" },
            new DNSRecord { Type = "MX", Name = "example.com" },
            new DNSRecord { Type = "A", Name = "no-such-domain.com" },
            new DNSRecord { Type = "A", Name = "bad_domain.com" }
        };

        int msgIdCounter = 2;

        foreach (var lookup in lookups)
        {
            Message lookupMessage = new Message { MsgId = msgIdCounter++, MsgType = MessageType.DNSLookup, Content = lookup };
            SendMessage(clientSocket, serverEndpoint, lookupMessage);

            var reply = ReceiveMessage(clientSocket);

            if (reply.MsgType == MessageType.DNSLookupReply)
            {
                //TODO: [Send Acknowledgment to Server]
                Message ackMessage = new Message { MsgId = msgIdCounter++, MsgType = MessageType.Ack, Content = reply.MsgId };
                SendMessage(clientSocket, serverEndpoint, ackMessage);
            }
        }

        // Wait for End Message
        ReceiveMessage(clientSocket);

        // TODO: [Create and send DNSLookup Message]
        static void SendMessage(Socket socket, IPEndPoint endpoint, Message message)
        {
            string json = JsonSerializer.Serialize(message);
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            socket.SendTo(buffer, endpoint);
            Console.WriteLine($"Sent: {json}");
        }

        //TODO: [Receive and print DNSLookupReply from server]
        static Message ReceiveMessage(Socket socket)
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = new byte[4096];
            int received = socket.ReceiveFrom(buffer, ref remoteEP);
            string json = Encoding.UTF8.GetString(buffer, 0, received);
            Message? message = JsonSerializer.Deserialize<Message>(json);
            Console.WriteLine($"Received: {json}");
            return message;
        }

    
    
        

        // TODO: [Send next DNSLookup to server]
        // repeat the process until all DNSLoopkups (correct and incorrect onces) are sent to server and the replies with DNSLookupReply

        //TODO: [Receive and print End from server]





    }
}