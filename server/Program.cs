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
        sUDP.start(); // Start de server
    }
}

class ServerUDP
{
    private Socket serverSocket; // De UDP socket waarop we gaan luisteren
    private EndPoint remoteEndpoint; // De client die ons iets stuurt
    private const int port = 11000; // De poort waarop de server draait

    //TODO: implement all necessary logic to create sockets and handle incoming messages
    // Do not put all the logic into one method. Create multiple methods to handle different tasks.
    public void start()
    {
        System.Console.WriteLine("Server word opgestart...");

        InitializeSocket(); // Socket opzetten en binden
        System.Console.WriteLine("Server luistert op poort " + port);

        // Ontvang eerste bericht (veracht: Hello)
        Message message = ReceiveMessage();

        // Als het een Hello-bericht is, reageren met Welcome
        if (message.Type == MessageType.Hello)
            HandleHello(message);
        else
            System.Console.WriteLine("Verwachtte Hello, maar kreeg iets anders: " + message.Type);

        System.Console.WriteLine("Server stopt (alleen voor de test).");
    }

    private void InitializeSocket()
    {
        // Setup de socket en koppel deze aan alle IP-adressen op poort 11000
        serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, port);
        serverSocket.Bind(localEndPoint);

        // De remote endpoint word automatisch gevuld zodra een bericht wordt ontvangen
        remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
    }

    private Message ReceiveMessage()
    {
        // Buffer voor inkomende berichten
        byte[] buffer = new byte[4096];

        // Ontvang bericht
        int received = serverSocket.ReceiveFrom(buffer, ref remoteEndpoint);

        // Decodeer de datat naar een JSON-string
        string jsonMessage = Encoding.UTF8.GetString(buffer, 0, received);
        System.Console.WriteLine("Bericht ontvangen:\n" + jsonMessage);

        // Parse de JSON naar een Message-object
        Message? message = JsonSerializer.Deserialize<Message>(jsonMessage);
        return message ?? throw new Exception("Kon bericht niet parsen.");
    }

    private void HandleHello(Message message)
    {
        System.Console.WriteLine("Hello ontvangen met MsgId " + message.MsgId);

        // Stuur een Welcome met een nieuwe ID terug
        SendWelcome(message.MsgId + 1);
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