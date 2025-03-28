using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        var client = new ClientUDP();

        foreach (var dnsLookup in testLookups)
        {
            client.start(dnsLookup);
        }
    }
}

class ClientUDP
{
    private const int ServerPort = 11000;
    private const int TimeoutMilliseconds = 10000;

    private readonly Socket _socket;
    private readonly IPEndPoint _serverEndpoint;
    private int messageIdCounter = 0;
    private int lastDNSLookupMsgId = -1;
    private bool handshakeDone = false;

    private int secondsLeft = 10;
    private System.Threading.Timer? countdownLogger;

    private enum ExpectedServerMessage
    {
        Welcome,
        DNSLookupReply,
        End
    }

    private ExpectedServerMessage expectedMessage = ExpectedServerMessage.Welcome;

    public ClientUDP()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _serverEndpoint = new IPEndPoint(IPAddress.Loopback, ServerPort);
    }

    public void start(DNSRecord dnsLookup)
    {
        try
        {
            Console.WriteLine("\n\n\nClient: Starting client met DNSlookup:\n");

            if (DoHandshake() == false) return;
            if (DoDNSLookup(dnsLookup) == false) return;
            if (DoAck() == false) return;
            //if (WaitForEnd() == false) return;

            Console.WriteLine("End ontvangen. Exiting client.\n\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client: Error: {ex.Message}");
        }
    }

    private bool DoHandshake()
    {
        if (handshakeDone)
            return true;

        var message = new Message
        {
            MsgId = ++messageIdCounter,
            Type = MessageType.Hello,
            Content = "Hello from client"
        };

        SendMessage(message);
        expectedMessage = ExpectedServerMessage.Welcome;

        bool ok = AwaitResponse(MessageType.Welcome, message.MsgId);
        if (ok)
            handshakeDone = true;

        return ok;
    }

    private bool DoDNSLookup(DNSRecord record)
    {
        var lookupMsg = new Message
        {
            MsgId = ++messageIdCounter,
            Type = MessageType.DNSLookup,
            Content = JsonSerializer.Serialize(record)
        };

        lastDNSLookupMsgId = lookupMsg.MsgId;

        SendMessage(lookupMsg);
        expectedMessage = ExpectedServerMessage.DNSLookupReply;

        return AwaitResponse(MessageType.DNSLookupReply, lastDNSLookupMsgId);
    }

    private bool DoAck()
    {
        var ack = new Message
        {
            MsgId = ++messageIdCounter,
            Type = MessageType.Ack,
            Content = lastDNSLookupMsgId.ToString()
        };

        SendMessage(ack);
        expectedMessage = ExpectedServerMessage.End;
        return true;
    }

    private bool WaitForEnd()
    {
        expectedMessage = ExpectedServerMessage.End;
        return AwaitResponse(MessageType.End);
    }

    private bool AwaitResponse(MessageType expectedType, int? expectedMsgId = null)
    {
        try
        {
            var message = WaitForMessage();

            if (message.Type == MessageType.End)
            {
                return HandleEnd(message);
            }

            if (message.Type != expectedType)
            {
                Console.WriteLine($"Fout: Verwacht '{expectedType}', kreeg '{message.Type}'");
                return false;
            }

            if (expectedMsgId.HasValue && message.MsgId != expectedMsgId.Value)
            {
                Console.WriteLine($"Fout: Verwachte MsgId {expectedMsgId}, kreeg {message.MsgId}");
                return false;
            }

            switch (message.Type)
            {
                case MessageType.Welcome: return HandleWelcome(message);
                case MessageType.DNSLookupReply: return HandleDNSLookupReply(message);
                default:
                    Console.WriteLine("Onverwacht berichttype.");
                    return false;
            }
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Timeout: Geen antwoord ontvangen. Client sluit af.");

            // Reset alleen de handshake als we die nog verwachtten
            if (expectedMessage == ExpectedServerMessage.Welcome)
                handshakeDone = false;

            return false;
        }
    }

    private Message WaitForMessage()
    {
        var buffer = new byte[4096];
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        secondsLeft = TimeoutMilliseconds / 1000;
        StartCountdownLog();

        DateTime start = DateTime.Now;
        DateTime deadline = start.AddMilliseconds(TimeoutMilliseconds);

        while (DateTime.Now < deadline)
        {
            try
            {
                if (_socket.Available > 0)
                {
                    StopCountdownLog();

                    int received = _socket.ReceiveFrom(buffer, ref remoteEP);
                    string receivedText = Encoding.UTF8.GetString(buffer, 0, received);
                    var message = JsonSerializer.Deserialize<Message>(receivedText)!;
                    LogMessage(message, "Received");
                    return message;
                }
            }
            catch (SocketException)
            {
                // Gewoon opnieuw proberen
            }

            Thread.Sleep(100); // korte rust om CPU te sparen
        }

        StopCountdownLog();
        throw new TimeoutException("Client: Geen antwoord ontvangen binnen timeout.");
    }

    private void StartCountdownLog()
    {
        secondsLeft = TimeoutMilliseconds / 1000;
        countdownLogger = new System.Threading.Timer(state =>
        {
            if (secondsLeft > 0)
            {
                Console.WriteLine($"⏳ Timeout over {secondsLeft} seconden...");
                secondsLeft--;
            }
        }, null, 0, 1000); // start direct, elke seconde aftellen
    }

    private void StopCountdownLog()
    {
        countdownLogger?.Dispose();
        countdownLogger = null;
    }

    private void SendMessage(Message message)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        _socket.SendTo(data, _serverEndpoint);
        LogMessage(message, "Sent");
    }

    private void LogMessage(Message message, string prefix = "Sent")
    {
        Console.WriteLine($"{prefix}: " + JsonSerializer.Serialize(message, new JsonSerializerOptions { WriteIndented = true }));
    }

    private bool HandleWelcome(Message msg)
    {
        if (msg.Type != MessageType.Welcome)
        {
            Console.WriteLine("Received: Expected Welcome message but got: " + msg.Type);
            return false;
        }

        Console.WriteLine("Received: " + msg.Content);
        return true;
    }

    private bool HandleDNSLookupReply(Message msg)
    {
        if (msg.Type != MessageType.DNSLookupReply)
        {
            Console.WriteLine("Received: Expected DNSLookupReply message but got: " + msg.Type);
            return false;
        }

        Console.WriteLine("Received: " + msg.Content);
        return true;
    }

    private bool HandleEnd(Message msg)
    {
        if (msg.Type != MessageType.End)
        {
            Console.WriteLine("Received: Expected End message but got: " + msg.Type);
            return false;
        }

        if (expectedMessage == ExpectedServerMessage.End)
        {
            Console.WriteLine("✅ Server heeft de verbinding correct afgesloten.");
        }
        else
        {
            Console.WriteLine($"⚠️  Server heeft de verbinding onverwacht verbroken. Verwacht werd: {expectedMessage}");
        }

        Console.WriteLine("Received: " + msg.Content);
        handshakeDone = false;
        return true;
    }
}

