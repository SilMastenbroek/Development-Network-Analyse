using System;
using System.Data.SqlTypes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MessageNS;
using System.Timers;

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
    private class ClientState
    {
        public EndPoint Endpoint { get; set; }
        public ExpectedStep currentStep { get; set; } = ExpectedStep.Hello;
        public int LastLookupMsgId { get; set; } = -1;
    }

    // De socket waarmee de UDP-berichten verzenden en ontvangen
    private Socket serverSocket;
    // Server luistert op poort 11000
    private const int port = 11000;
    private EndPoint Endpoint;
    private ExpectedStep currentStep = ExpectedStep.Hello;
    private enum ExpectedStep { Hello, Lookup, Ack }
    private Dictionary<string, ClientState> clients = new();
    // Timeout logica
    private System.Timers.Timer timeoutTimer;
    // 10 seconden in ms
    private const double timeoutDuration = 10000;
    // Voorkomt dubbele End
    private bool isTimeoutActive = false;
    private System.Threading.Timer? countdownLogger;
    private int secondsLeft = 10;

    // Hoofdloop van server
    public void start()
    {
        System.Console.WriteLine("Server word opgestart...");
        InitializeSocket(); // Socket opzetten en binden
        System.Console.WriteLine("Server luistert op poort " + port);

        // Timer instellen voor inactivity
        timeoutTimer = new System.Timers.Timer(timeoutDuration);
        timeoutTimer.Elapsed += OnTimeoutReached;
        timeoutTimer.AutoReset = false;
        timeoutTimer.Start();

        while (true)
        {
            try
            {
                // Ontvang binnendkomend bericht
                Message message = ReceiveMessage();

                // Haal client-informatie op
                string clientKey = Endpoint.ToString();
                ClientState client = clients[clientKey];

                // Reset timer bij elk geldig bericht
                timeoutTimer.Stop();
                timeoutTimer.Start();
                isTimeoutActive = false;

                StopCountdownLog(); // stop vorige log-timer (indien actief)
                StartCountdownLog(); // start nieuwe aftel-timer

                // Genegeerd (bijvoorbeeld een eigen bericht)
                if (message.MsgId == -1)
                    continue;

                switch (message.MsgType)
                {
                    // Server verwacht een Hello van de client
                    case MessageType.Hello:
                        if (client.currentStep != ExpectedStep.Hello)
                        {
                            // Alles behalve Hello is ongeldig in deze stap
                            SendError(message.MsgId, "Verwacht Hello als eerste bericht.");
                            break;
                        }

                        // Hello ontvangen stuur Welcome terug
                        HandleHello(message);
                        // Ga naar de volgende stap: nu verwachten we een DNSlookup
                        client.currentStep = ExpectedStep.Lookup;
                        break;

                    // Server verwacht een DNSLookup van de client
                    case MessageType.DNSLookup:
                        if (client.currentStep != ExpectedStep.Lookup)
                        {
                            // Geen DNSLookup terwijl dat wel verwacht werd
                            SendError(message.MsgId, "Verwacht DNSLookup na Hello.");
                            break;
                        }

                        client.LastLookupMsgId = message.MsgId;
                        // Verwerk de DNSLookup
                        HandleDNSLookup(message, client);
                        client.currentStep = ExpectedStep.Ack;
                        break;

                    // Server verwacht een Ack ter bevestiging van de DNSReply
                    case MessageType.Ack:
                        if (client.currentStep != ExpectedStep.Ack)
                        {
                            // iets anders dan Ack ontvangen
                            SendError(message.MsgId, "Verwacht Ack na DNSLookupReply.");
                            break;
                        }

                        int ackedMsgId = int.TryParse(message.Content?.ToString(), out var id) ? id : -1;
                        if (ackedMsgId != client.LastLookupMsgId)
                        {
                            SendError(message.MsgId, "Ack komt niet overeen met laatste Lookup MsgId.");
                        }
                        else
                        {
                            // Ack ontvangen, sessie voor deze lookup is afgerond
                            HandleAck(message);
                            SendEnd(client.Endpoint);
                            // Server staat weer klaar voor nieuwe DNSLookup (zelfde sessie)
                            client.currentStep = ExpectedStep.Lookup;
                        }
                        break;

                    default:
                        SendError(message.MsgId, "Onbekend berichttype ontvangen.");
                        break;
                }
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
        Endpoint = new IPEndPoint(IPAddress.Any, 0);
    }

    private Message ReceiveMessage()
    {
        byte[] buffer = new byte[4096];
        EndPoint senderEndpoint = new IPEndPoint(IPAddress.Any, 0);
        int received = serverSocket.ReceiveFrom(buffer, ref senderEndpoint);

        // Herken unieke client via IP + poort
        string clientKey = senderEndpoint.ToString();

        // Als deze client onbekend is, voeg toe
        if (!clients.ContainsKey(clientKey))
            clients[clientKey] = new ClientState { Endpoint = senderEndpoint };

        Endpoint = senderEndpoint;

        // Filter: voorkom dat de server zijn eigen bericht opvangt
        if (senderEndpoint is IPEndPoint remoteIp &&
            remoteIp.Address.Equals(IPAddress.Loopback) &&
            remoteIp.Port == ((IPEndPoint)serverSocket.LocalEndPoint!).Port)
        {
            Console.WriteLine("Eigen bericht genegeerd.");
            return new Message { MsgId = -1, MsgType = MessageType.Error, Content = "Echo ignored" };
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
            MsgType = MessageType.Welcome,
            Content = "Welkom van de server!"
        };

        // Zet om naar JSON en verstuur naar de client
        string json = JsonSerializer.Serialize(welcome);
        byte[] data = Encoding.UTF8.GetBytes(json);
        serverSocket.SendTo(data, Endpoint);

        System.Console.WriteLine("Welkom verzonden!");
    }

    private void HandleDNSLookup(Message message, ClientState client)
    {
        System.Console.WriteLine("DNSLookup ontvangen met MsgId " + message.MsgId);

        try
        {
            // Parse content als DNSRecord (Type + Name)
            var lookupRequest = JsonSerializer.Deserialize<DNSRecord>(message.Content.ToString() ?? "");
            string type = lookupRequest.Type;
            string name = lookupRequest.Name;

            // Validate: incomplete lookup?
            if (lookupRequest == null || string.IsNullOrEmpty(lookupRequest.Type) || string.IsNullOrEmpty(lookupRequest.Name))
            {
                SendError(message.MsgId, "Ongeldige lookup content.");
                client.currentStep = ExpectedStep.Lookup; // herstel correcte stap
                return;
            }

            // Laad DNS-records uit JSON bestand
            string jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "dns_records.json");
            if (!File.Exists(jsonPath))
            {
                SendError(message.MsgId, "DNS bestand niet gevonden", client.Endpoint);
                client.currentStep = ExpectedStep.Lookup; // herstel correcte stap
                return;
            }

            string json = File.ReadAllText(jsonPath);
            List<DNSRecord>? records = JsonSerializer.Deserialize<List<DNSRecord>>(json);

            if (records == null)
            {
                SendError(message.MsgId, "Kan DNS-bestand niet inlezen.", client.Endpoint);
                return;
            }

            // Zoek naar een match (Type en Name)
            var match = records.FirstOrDefault(r =>
                r.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                // Record gevonden: stuur DNSLookupReply
                Message reply = new Message
                {
                    MsgId = message.MsgId,
                    MsgType = MessageType.DNSLookupReply,
                    Content = JsonSerializer.Serialize(match)
                };

                SendMessage(reply, client.Endpoint);
                Console.WriteLine("DNSLookupReply verzonden.");
            }
            else
            {
                SendError(message.MsgId, $"Geen DNS-record gevonden voor {name}", client.Endpoint);
                client.currentStep = ExpectedStep.Lookup; // herstel correcte stap
            }
        }
        catch
        {
            SendError(message.MsgId, "Ongeldige lookup content.", client.Endpoint);
            client.currentStep = ExpectedStep.Lookup; // herstel correcte stap
        }
    }

    public void SendError(int originalMsgId, string errorMessage)
    {
        SendError(originalMsgId, errorMessage, Endpoint);
    }

    public void SendError(int originalMsgId, string errorMessage, EndPoint ep)
    {
        Message error = new Message
        {
            MsgId = originalMsgId,
            MsgType = MessageType.Error,
            Content = errorMessage
        };

        SendMessage(error, ep);
        System.Console.WriteLine("Error verzonden: " + errorMessage);
    }

    public void HandleAck(Message message)
    {
        System.Console.WriteLine("Ack ontvangen voor MsgId: " + message.Content);
    }

    private void OnTimeoutReached(object? sender, ElapsedEventArgs e)
    {
        if (!isTimeoutActive)
        {
            isTimeoutActive = true;
            Console.WriteLine("Timeout: Geen bericht ontvangen in 10 seconden. Verstuur End.");
            SendEnd();
        }
    }

    private void StartCountdownLog()
    {
        secondsLeft = 10;
        countdownLogger = new System.Threading.Timer(state =>
        {
            if (secondsLeft > 0)
            {
                Console.WriteLine($"⏳ Timeout over {secondsLeft} seconden...");
                secondsLeft--;
            }
        }, null, 0, 1000); // start meteen, tick elke 1000ms
    }

    private void StopCountdownLog()
    {
        countdownLogger?.Dispose();
    }

    private void SendMessage(Message message, EndPoint target)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        serverSocket.SendTo(data, target);
    }

    private void SendEnd()
    {
        Message endMessage = new Message
        {
            MsgId = 9999,
            MsgType = MessageType.End,
            Content = "Alle lookups afgehandeld"
        };

        string json = JsonSerializer.Serialize(endMessage);
        byte[] data = Encoding.UTF8.GetBytes(json);
        serverSocket.SendTo(data, Endpoint);

        System.Console.WriteLine("End verzonden naar client");
    }

    private void SendEnd(EndPoint ep)
    {
        Message endMessage = new Message
        {
            MsgId = 9999,
            MsgType = MessageType.End,
            Content = "Alle lookups afgehandeld"
        };

        SendMessage(endMessage, ep);

        System.Console.WriteLine("End verzonden naar client");
    }
}