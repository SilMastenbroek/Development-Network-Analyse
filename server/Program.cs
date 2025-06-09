using System;
using System.Data;
using System.Data.SqlTypes;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using LibData;

// ReceiveFrom();
class Program
{
    static void Main(string[] args)
    {
        ServerUDP.start();
    }
}

public class Setting
{
    public int ServerPortNumber { get; set; }
    public string? ServerIPAddress { get; set; }
    public int ClientPortNumber { get; set; }
    public string? ClientIPAddress { get; set; }
}


class ServerUDP
{
    static string configFile = @"../Setting.json";
    static string configContent = File.ReadAllText(configFile);
    static Setting? setting = JsonSerializer.Deserialize<Setting>(configContent);

    // TODO: [Read the JSON file and return the list of DNSRecords]
    static DNSRecord[]? dNSRecords = JsonSerializer.Deserialize<DNSRecord[]>(File.ReadAllText(@"dns_records.json"));

    // (zelf toegevoegd) Define the steps for the server state machine
    enum ServerStep { AwaitHello, AwaitLookup, AwaitAck }
    static ServerStep currentStep = ServerStep.AwaitHello;
    static System.Timers.Timer inactivityTimer;
    static int countdown;


    public static void start()
    {
        // TODO: [Create a socket and endpoints and bind it to the server IP address and port number]

        // Maak een IPEndPoint-object voor het binden van de socket aan het IP-adres en de poort van de server.
        IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(setting.ServerIPAddress!), setting.ServerPortNumber);

        // Creëer een UDP-socket die gebruikmaakt van IPv4 (AddressFamily.InterNetwork), UDP (SocketType.Dgram) transportlaag en het UDP-protocol.
        Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Bind de aangemaakte socket aan het opgegeven IP-adres en de poort zodat de server luisterend klaarstaat.
        serverSocket.Bind(serverEndpoint);
        Console.WriteLine($"Server listening on {serverEndpoint}");

        // Maak een EndPoint-object aan dat wordt gebruikt om het IP-adres en de poort van de inkomende client-berichten te ontvangen.
        EndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[4096];

        inactivityTimer = new System.Timers.Timer(1000); // 1 second interval
        inactivityTimer.Elapsed += (sender, e) =>
        {
            countdown--;
            Console.WriteLine($"Time remaining: {countdown} seconds");
            if (countdown <= 0)
            {
                Message endMessage = new Message { MsgId = 9999, MsgType = MessageType.End, Content = "End due to inactivity" };
                SendMessage(serverSocket, endMessage, clientEP);
                currentStep = ServerStep.AwaitHello;
                Console.WriteLine("Inactivity timeout. Sent End message.");
                inactivityTimer.Stop();
            }
        };

        ResetTimer();

        // TODO:[Receive and print a received Message from the client]




        // TODO:[Receive and print Hello]



        // TODO:[Send Welcome to the client]


        // TODO:[Receive and print DNSLookup]


        // TODO:[Query the DNSRecord in Json file]

        // TODO:[If found Send DNSLookupReply containing the DNSRecord]



        // TODO:[If not found Send Error]


        // TODO:[Receive Ack about correct DNSLookupReply from the client]


        // TODO:[If no further requests receieved send End to the client]

    }
    
    static void ResetTimer() // Zelf toegevoegd
    {
        countdown = 10;
        inactivityTimer.Stop();
        inactivityTimer.Start();
        Console.WriteLine($"Timer reset to {countdown} seconds.");
    }


}