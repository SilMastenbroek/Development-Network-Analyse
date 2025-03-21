using System;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using MessageNS;


// Do not modify this class
class Program
{
    static void Main(string[] args)
    {
        ServerUDP sUDP = new ServerUDP();
        sUDP.start();
    }
}

class ServerUDP
{
    private Socket serverSocket;
    private EndPoint remoteEndpoint;
    private const int port = 11000;

    //TODO: implement all necessary logic to create sockets and handle incoming messages
    // Do not put all the logic into one method. Create multiple methods to handle different tasks.
    public void start()
    {
        System.Console.WriteLine("Server word opgestart...");

        InitializeSocket();
        System.Console.WriteLine("Server luistert op poort " + port);

        // 1 keer Hello afhandelen (je gaat dit later in een loop zetten)
        Message message = ReceiveMessage();
        if (message.MsgType == MessageType.Hello)
            HandleHello(message);
        else
            System.Console.WriteLine("Verwachtte Hello, maar kreeg iets anders: " + message.MsgType);

        System.Console.WriteLine("Server stopt (alleen voor de test).");
    }

    private void InitializeSocket()
    {
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
        serverSocket.Bind(localEndPoint);

        remoteEndpoint = new IPEndPoint(IPAddress.Any, 0); // Vult zichzelf in bij ReceiveForm
    }

    private Message ReceiveMessage()
    {
        byte[] buffer = new byte[4096];
        int received = serverSocket.ReceiveFrom(buffer, ref remoteEndpoint);
        string jsonMessage = Encoding.UTF8.GetString(buffer, 0, received);

        System.Console.WriteLine("Bericht ontvangen: ");
        System.Console.WriteLine(jsonMessage);

        Message? message = JsonSerializer.Deserialize<Message>(jsonMessage);
        return message ?? throw new Exception("Kon bericht niet parsen.");
    }

    private void HandleHello(Message message)
    {
        System.Console.WriteLine("Hello ontvangen met MsgId " + message.MsgId);

        SendWelcome(message.MsgId + 1);
    }

    private void SendWelcome(int replyMsgId)
    {
        Message welcome = new Message
        {
            MsgId = replyMsgId,
            MsgType = MessageType.Welcome,
            Content = "Welkom van de server!"
        };

        string json = JsonSerializer.Serialize(welcome);
        byte[] data = Encoding.UTF8.GetBytes(json);
        serverSocket.SendTo(data, remoteEndpoint);

        System.Console.WriteLine("Welkom verzonden!");
    }

    //TODO: create all needed objects for your sockets 

    //TODO: keep receiving messages from clients
    // you can call a dedicated method to handle each received type of messages

    //TODO: [Receive Hello]

    //TODO: [Send Welcome]

    //TODO: [Receive RequestData]

    //TODO: [Send Data]

    //TODO: [Implement your slow-start algorithm considering the threshold] 

    //TODO: [End sending data to client]

    //TODO: [Handle Errors]

    //TODO: create all needed methods to handle incoming messages


}