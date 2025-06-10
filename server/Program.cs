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
    static DNSRecord[]? dNSRecords = JsonSerializer.Deserialize<DNSRecord[]>(File.ReadAllText(@"dns_records.json"));

    enum ServerStep { AwaitHello, AwaitLookup, AwaitAck }
    static ServerStep currentStep = ServerStep.AwaitHello;
    static System.Timers.Timer inactivityTimer;
    static int countdown;

    static Message? lastSentReply = null;
    static int retryCount = 0;

    public static void start()
    {
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
                if (!serverSocket.Poll(1000000, SelectMode.SelectRead)) // 1 sec timeout
                {
                    continue;
                }

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
                        if (receivedMsg.MsgType == MessageType.Hello)
                        {
                            Console.WriteLine("Hello received from client.");
                            Message welcome = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Welcome, Content = "Welcome from server" };
                            SendMessage(serverSocket, welcome, clientEP);
                            currentStep = ServerStep.AwaitLookup;
                        }
                        break;

                    case ServerStep.AwaitLookup:
                        if (receivedMsg.MsgType == MessageType.DNSLookup)
                        {
                            Console.WriteLine("DNSLookup received from client.");
                            DNSRecord? requestedRecord = JsonSerializer.Deserialize<DNSRecord>(receivedMsg.Content!.ToString()!);

                            if (requestedRecord == null || string.IsNullOrWhiteSpace(requestedRecord.Name) || string.IsNullOrWhiteSpace(requestedRecord.Type))
                            {
                                Message error = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Incomplete DNSLookup" };
                                SendMessage(serverSocket, error, clientEP);
                                break;
                            }

                            if (!IsValidDomain(requestedRecord.Name))
                            {
                                Message error = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Invalid domain format" };
                                SendMessage(serverSocket, error, clientEP);
                                break;
                            }

                            var foundRecord = dNSRecords?.FirstOrDefault(r =>
                                r.Type == requestedRecord.Type &&
                                (NormalizeDomain(r.Name) == NormalizeDomain(requestedRecord.Name))
                            );

                            static string NormalizeDomain(string domain)
                            {
                                if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                                    return domain.Substring(4);
                                return domain;
                            }

                            if (foundRecord != null)
                            {
                                Message reply = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.DNSLookupReply, Content = foundRecord };
                                lastSentReply = reply;
                                retryCount = 0;
                                SendMessage(serverSocket, lastSentReply, clientEP);
                                currentStep = ServerStep.AwaitAck;
                            }
                            else
                            {
                                Message notFound = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Domain not found" };
                                SendMessage(serverSocket, notFound, clientEP);
                            }
                        }
                        break;

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
                                Thread.Sleep(2000);
                                if (lastSentReply != null)
                                    SendMessage(serverSocket, lastSentReply, clientEP);
                            }
                            else
                            {
                                Console.WriteLine("Max retries reached. Returning to AwaitHello.");
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
        var regex = new System.Text.RegularExpressions.Regex(@"^(www\.)?([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}$");
        return regex.IsMatch(domain);
    }
}
