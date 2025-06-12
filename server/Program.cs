using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using LibData;

// Entry point van de applicatie
class Program
{
    static void Main(string[] args)
    {
        ServerUDP.start(); // Start de servercommunicatie
    }
}

// Klasse voor instellingen uit settings.json
public class Settings
{
    public int ServerPortNumber { get; set; } // Poort waarop de server luistert
    public string? ServerIPAddress { get; set; } // IP-adres waarop de server bindt
}

class ServerUDP
{
    // TODO: [Read the JSON file and return the list of DNSRecords]
    // Laad serverinstellingen en DNS-records in vanuit JSON-bestanden
    static Settings? settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(@"settings.json"));
    static DNSRecord[]? dNSRecords = JsonSerializer.Deserialize<DNSRecord[]>(File.ReadAllText(@"dns_records.json"));

    // Enum om aan te geven welke stap in het protocol actief is
    enum ServerStep { AwaitHello, AwaitLookup, AwaitAck }
    static ServerStep currentStep = ServerStep.AwaitHello;

    // Timer voor het controleren op inactiviteit
    static System.Timers.Timer inactivityTimer;
    static int countdown;

    // Variabelen voor retry-logica bij geen ACK
    static Message? lastSentReply = null;
    static int retryCount = 0;

    public static void start()
    {
        // TODO: [Create a socket and endpoints and bind it to the server IP address and port number]
        // Maak een UDP socket en bind deze aan het IP en poort uit settings.json
        IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(settings.ServerIPAddress!), settings.ServerPortNumber);
        Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(serverEndpoint);
        Console.WriteLine($"Server listening on {serverEndpoint}");

        // Buffer en client endpoint-voorbereiding
        EndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[4096];

        // Inactiviteitstimer initialiseren (elke seconde aftellen vanaf 10)
        countdown = 10;
        inactivityTimer = new System.Timers.Timer(1000);
        inactivityTimer.Elapsed += (s, e) =>
        {
            countdown--;
            Console.WriteLine($"Countdown: {countdown}s");
            if (countdown <= 0)
            {
                Console.WriteLine("Timeout: No message received in 10 seconds. Sending End.");
                Message timeoutMsg = new Message { MsgId = 0, MsgType = MessageType.End, Content = "End due to inactivity" };
                try
                {
                    SendMessage(serverSocket, timeoutMsg, clientEP);
                }
                catch (SocketException se)
                {
                    Console.WriteLine($"Failed to send timeout message: {se.Message}");
                }
                catch (ObjectDisposedException ode)
                {
                    Console.WriteLine($"Socket disposed: {ode.Message}");
                }
                currentStep = ServerStep.AwaitHello;
                inactivityTimer.Stop();
            }
        };

        while (true)
        {
            try
            {
                // Wacht maximaal 1 seconde op een bericht
                if (!serverSocket.Poll(1000000, SelectMode.SelectRead))
                {
                    continue;
                }

                // TODO:[Receive and print a received Message from the client]
                // Lees inkomend bericht en reset de timer
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
                    case ServerStep.AwaitHello:
                        // TODO:[Receive and print Hello]
                        // Verwerk Hello-bericht
                        if (receivedMsg.MsgType == MessageType.Hello)
                        {
                            Console.WriteLine("Hello received from client.");
                            // TODO:[Send Welcome to the client]
                            Message welcome = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Welcome, Content = "Welcome from server" };
                            SendMessage(serverSocket, welcome, clientEP);
                            currentStep = ServerStep.AwaitLookup;
                        }
                        break;

                    case ServerStep.AwaitLookup:
                        // TODO:[Receive and print DNSLookup]
                        // Verwerk DNSLookup-bericht
                        if (receivedMsg.MsgType == MessageType.DNSLookup)
                        {
                            Console.WriteLine("DNSLookup received from client.");
                            DNSRecord? requestedRecord = JsonSerializer.Deserialize<DNSRecord>(receivedMsg.Content!.ToString()!);

                            // Controleer op ontbrekende velden
                            if (requestedRecord == null || string.IsNullOrWhiteSpace(requestedRecord.Name) || string.IsNullOrWhiteSpace(requestedRecord.Type))
                            {
                                Message error = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Incomplete DNSLookup" };
                                SendMessage(serverSocket, error, clientEP);
                                break;
                            }

                            // Valideer domeinstructuur
                            if (!IsValidDomain(requestedRecord.Name))
                            {
                                Message error = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Invalid domain format" };
                                SendMessage(serverSocket, error, clientEP);
                                break;
                            }

                            // TODO:[Query the DNSRecord in Json file]
                            // Zoek naar overeenkomend DNS-record (type en genormaliseerde naam)
                            var foundRecord = dNSRecords?.FirstOrDefault(r =>
                                r.Type == requestedRecord.Type &&
                                (NormalizeDomain(r.Name).Equals(NormalizeDomain(requestedRecord.Name), StringComparison.OrdinalIgnoreCase))
                            );

                            // Helper om www. te verwijderen
                            static string NormalizeDomain(string domain)
                            {
                                if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                                    return domain.Substring(4);
                                return domain;
                            }

                            // Verzend het record of foutmelding
                            // TODO:[If found Send DNSLookupReply containing the DNSRecord]
                            if (foundRecord != null)
                            {
                                Message reply = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.DNSLookupReply, Content = foundRecord };
                                lastSentReply = reply;
                                retryCount = 0;
                                SendMessage(serverSocket, lastSentReply, clientEP);
                                currentStep = ServerStep.AwaitAck;
                            }
                            // TODO:[If not found Send Error]
                            else
                            {
                                Message notFound = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Domain not found" };
                                SendMessage(serverSocket, notFound, clientEP);
                            }
                        }
                        break;

                    case ServerStep.AwaitAck:
                        // TODO:[Receive Ack about correct DNSLookupReply from the client]
                        // Verwerk ACK of herzend bij geen ACK
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
                                Thread.Sleep(2000);
                                if (lastSentReply != null)
                                    SendMessage(serverSocket, lastSentReply, clientEP);
                            }
                            else
                            {
                                Console.WriteLine("Max retries reached. Returning to AwaitHello.");
                                // TODO:[If no further requests receieved send End to the client]
                                Message endMsg = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.End, Content = "Max retries without Ack" };
                                SendMessage(serverSocket, endMsg, clientEP);
                                currentStep = ServerStep.AwaitHello;
                                retryCount = 0;
                                lastSentReply = null;
                            }
                        }
                        break;
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket exception occurred: {ex.Message}");
                currentStep = ServerStep.AwaitHello;
                inactivityTimer.Stop();
            }
            catch (ObjectDisposedException ex)
            {
                Console.WriteLine($"Socket has been disposed: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected exception: {ex.Message}");
                currentStep = ServerStep.AwaitHello;
                inactivityTimer.Stop();
            }
        }
    }

    // Verzenden van berichten via socket
    static void SendMessage(Socket socket, Message message, EndPoint client)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        socket.SendTo(data, client);
        Console.WriteLine($"Sent: {json}");
    }

    // Timer resetten naar 10 seconden
    static void ResetTimer()
    {
        countdown = 10;
        inactivityTimer.Stop();
        inactivityTimer.Start();
        Console.WriteLine($"Timer reset to 10 seconds");
    }

    // Regex-validatie van domeinnamen
    static bool IsValidDomain(string domain)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"^(www\.)?([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}$");
        return regex.IsMatch(domain);
    }
}
