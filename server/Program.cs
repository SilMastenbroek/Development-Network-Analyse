using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using LibData;

class Program
{
    static void Main(string[] args)
    {
        ServerUDP.start();
    }
}

public class Settings
{
    public int ServerPortNumber { get; set; }
    public string? ServerIPAddress { get; set; }
}


class ServerUDP
{
    static Settings? settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(@"settings.json"));

    // TODO: [Read the JSON file and return the list of DNSRecords]
    // Lees het DNS-recordbestand in (geformatteerd als JSON-array) dat gebruikt wordt voor DNS-queries.
    static DNSRecord[]? dNSRecords = JsonSerializer.Deserialize<DNSRecord[]>(File.ReadAllText(@"dns_records.json"));

    enum ServerStep { AwaitHello, AwaitLookup, AwaitAck }
    static ServerStep currentStep = ServerStep.AwaitHello;
    static System.Timers.Timer inactivityTimer;
    static int countdown;

    // Houdt bij welke MsgId de server als laatst heeft gebruikt
    static int serverMsgIdCounter = 1;
    static Message? lastSentReply = null;
    static int retryCount = 0;


    public static void start()
    {
        // TODO: [Create a socket and endpoints and bind it to the server IP address and port number]
        // Maak socket aan en bind deze aan het IP-adres/poort die vanuit Setting.json komt.
        IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(settings.ServerIPAddress!), settings.ServerPortNumber);
        Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(serverEndpoint);
        Console.WriteLine($"Server listening on {serverEndpoint}");

        EndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[4096];

        countdown = 10;
        inactivityTimer = new System.Timers.Timer(1000);
        inactivityTimer.Elapsed += (s, e) =>
        {
            countdown--;
            Console.WriteLine($"Countdown: {countdown}s");
            if (countdown <= 0)
            {
                Console.WriteLine("Timeout: No message received in 10 seconds. Sending End.");
                Message timeoutMsg = new Message { MsgId = serverMsgIdCounter++, MsgType = MessageType.End, Content = "End due to inactivity" };
                SendMessage(serverSocket, timeoutMsg, clientEP);
                currentStep = ServerStep.AwaitHello;
                inactivityTimer.Stop();
            }
        };

        while (true)
        {
            // TODO:[Receive and print a received Message from the client]
            // Ontvang bericht van client via UDP-socket
            int receivedBytes = serverSocket.ReceiveFrom(buffer, ref clientEP);
            string receivedJson = Encoding.UTF8.GetString(buffer, 0, receivedBytes);
            Console.WriteLine($"Received: {receivedJson}");
            ResetTimer();

            Message? receivedMsg = JsonSerializer.Deserialize<Message>(receivedJson);
            if (receivedMsg == null)
            {
                Console.WriteLine("Invalid message format");
                continue;
            }

            switch (currentStep)
            {
                // TODO:[Receive and print Hello]
                case ServerStep.AwaitHello:
                    if (receivedMsg.MsgType == MessageType.Hello)
                    {
                        Console.WriteLine("Hello received from client.");

                        // TODO:[Send Welcome to the client]
                        Message welcome = new Message { MsgId = serverMsgIdCounter++, MsgType = MessageType.Welcome, Content = "Welcome from server" };
                        SendMessage(serverSocket, welcome, clientEP);
                        currentStep = ServerStep.AwaitLookup;
                    }
                    break;

                // TODO:[Receive and print DNSLookup]
                case ServerStep.AwaitLookup:
                    if (receivedMsg.MsgType == MessageType.DNSLookup)
                    {
                        Console.WriteLine("DNSLookup received from client.");

                        DNSRecord? requestedRecord = JsonSerializer.Deserialize<DNSRecord>(receivedMsg.Content!.ToString()!);

                        if (requestedRecord == null || string.IsNullOrWhiteSpace(requestedRecord.Name) || string.IsNullOrWhiteSpace(requestedRecord.Type))
                        {
                            // TODO:[If not found Send Error]
                            Message error = new Message { MsgId = serverMsgIdCounter++, MsgType = MessageType.Error, Content = "Incomplete DNSLookup" };
                            SendMessage(serverSocket, error, clientEP);
                            break;
                        }

                        if (!IsValidDomain(requestedRecord.Name))
                        {
                            Message error = new Message { MsgId = serverMsgIdCounter++, MsgType = MessageType.Error, Content = "Invalid domain format" };
                            SendMessage(serverSocket, error, clientEP);
                            break;
                        }

                        // TODO:[Query the DNSRecord in Json file]
                        var foundRecord = dNSRecords?.FirstOrDefault(r => r.Type == requestedRecord.Type && r.Name == requestedRecord.Name);

                        if (foundRecord != null)
                        {
                            // TODO:[If found Send DNSLookupReply containing the DNSRecord]
                            Message reply = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.DNSLookupReply, Content = foundRecord };
                            lastSentReply = reply;
                            retryCount = 0;
                            SendMessage(serverSocket, lastSentReply, clientEP);
                            currentStep = ServerStep.AwaitAck;
                        }
                        else
                        {
                            // TODO:[If not found Send Error]
                            Message notFound = new Message { MsgId = serverMsgIdCounter++, MsgType = MessageType.Error, Content = "Domain not found" };
                            SendMessage(serverSocket, notFound, clientEP);
                        }
                    }
                    break;

                // TODO:[Receive Ack about correct DNSLookupReply from the client]
                case ServerStep.AwaitAck:
                    if (receivedMsg.MsgType == MessageType.Ack)
                    {
                        Console.WriteLine($"ACK received for MsgId {receivedMsg.Content}");
                        retryCount = 0;
                        lastSentReply = null;
                        currentStep = ServerStep.AwaitLookup;
                    }
                    else
                    {
                        retryCount++;
                        if (retryCount <= 3)
                        {
                            Console.WriteLine($"No ACK received. Retrying {retryCount}/3...");
                            Thread.Sleep(2000); // wacht 2 seconden
                            if (lastSentReply != null)
                                SendMessage(serverSocket, lastSentReply, clientEP);
                        }
                        else
                        {
                            Console.WriteLine("Max retries reached. Returning to AwaitHello.");
                            Message endMsg = new Message { MsgId = serverMsgIdCounter++, MsgType = MessageType.End, Content = "Max retries without Ack" };
                            SendMessage(serverSocket, endMsg, clientEP);
                            currentStep = ServerStep.AwaitHello;
                            retryCount = 0;
                            lastSentReply = null;
                        }
                    }
                    break;
            }
        }
    }

    static void SendMessage(Socket socket, Message message, EndPoint client)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        socket.SendTo(data, client);
        Console.WriteLine($"Sent: {json}");
    }

    static void ResetTimer()
    {
        countdown = 10;
        inactivityTimer.Stop();
        inactivityTimer.Start();
        Console.WriteLine($"Timer reset to 10 seconds");
    }
    
    static bool IsValidDomain(string domain)
    {
        // Regex: domeinnaam moet uit letters/cijfers bestaan, minimaal 1 punt bevatten, en geldig opgebouwd zijn
        var regex = new System.Text.RegularExpressions.Regex(@"^(www\.)?[a-zA-Z0-9-]+\.[a-zA-Z]{2,}$");
        return regex.IsMatch(domain);
    }
}
