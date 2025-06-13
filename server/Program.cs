using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LibData;

// TODO: [Start the server]
// Entry point for the DNS server application
class Program
{
    static void Main(string[] args)
    {
        // Start the UDP server
        ServerUDP.start();
    }
}

// Class to store server settings such as IP address and port
public class Settings
{
    public int ServerPortNumber { get; set; }
    public string? ServerIPAddress { get; set; }
}

class ServerUDP
{
    // TODO: [Read the JSON file and return the list of DNSRecords]
    // Deserialize settings and DNS records from respective JSON files
    static Settings? settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText("settings.json"));
    static DNSRecord[]? dNSRecords = JsonSerializer.Deserialize<DNSRecord[]>(File.ReadAllText("dns_records.json"));

    // Enumeration to manage the protocol steps
    enum ServerStep { AwaitHello, AwaitLookup, AwaitAck }
    static ServerStep currentStep = ServerStep.AwaitHello;

    // Timer and state variables for handling timeouts and retries
    static System.Timers.Timer inactivityTimer;
    static int countdown;
    static int timeoutDuration = 10; // Timeout in seconds
    static Message? lastSentReply = null; // Stores the last DNSLookupReply message sent
    static int retryCount = 0; // Number of times the last message has been retried
    static bool expectingAck = false; // Tracks whether server is waiting for an ACK

    public static void start()
    {
        // TODO: [Create a socket and endpoints and bind it to the server IP address and port number]
        // Create UDP socket and bind it to the configured IP and port
        IPEndPoint serverEndpoint = new IPEndPoint(IPAddress.Parse(settings.ServerIPAddress!), settings.ServerPortNumber);
        Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        serverSocket.Bind(serverEndpoint);
        Console.WriteLine($"Server listening on {serverEndpoint}");

        EndPoint clientEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[4096];

        // TODO: [Initialize inactivity timer logic for ACK timeout and END control]
        // Initialize inactivity timer that checks for missing ACKs and sends END messages
        countdown = timeoutDuration;
        inactivityTimer = new System.Timers.Timer(1000); // Tick every second
        inactivityTimer.Elapsed += (s, e) =>
        {
            countdown--;
            Console.WriteLine($"Countdown: {countdown}s");
            if (countdown <= 0)
            {
                inactivityTimer.Stop();

                // Retry sending the DNSLookupReply if ACK has not yet been received
                if (expectingAck && lastSentReply != null && retryCount < 3)
                {
                    retryCount++;
                    Console.WriteLine($"Timeout reached. Retrying message ({retryCount}/3)");
                    SendMessage(serverSocket, lastSentReply, clientEP);
                    countdown = timeoutDuration;
                    inactivityTimer.Start();
                }
                else
                {
                    // Either no ACK expected or maximum retries reached: end session
                    Console.WriteLine("Timeout exceeded or max retries reached. Sending END message to client.");
                    // TODO: [If no further requests receieved send End to the client]
                    Message timeoutMsg = new Message { MsgId = 0, MsgType = MessageType.End, Content = "End due to inactivity or failed ACK retries" };
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
                    // TODO: [Reset internal state after timeout or END]
                    // Reset state for next client session
                    currentStep = ServerStep.AwaitHello;
                    retryCount = 0;
                    lastSentReply = null;
                    expectingAck = false;
                }
            }
        };

        // Begin the main loop to listen for and handle incoming client messages
        while (true)
        {
            try
            {
                // Wait for incoming message (1 second poll)
                if (!serverSocket.Poll(1000000, SelectMode.SelectRead))
                {
                    continue;
                }

                // TODO: [Receive and print a received Message from the client]
                int receivedBytes = serverSocket.ReceiveFrom(buffer, ref clientEP);
                string receivedJson = Encoding.UTF8.GetString(buffer, 0, receivedBytes);

                try
                {
                    // TODO: [Try to preview the incoming message content for logging]
                    // Attempt to deserialize and preview the received message for debugging/logging
                    Message? preview = JsonSerializer.Deserialize<Message>(receivedJson);
                    Console.WriteLine($"Received: MsgId={preview?.MsgId}, MsgType={preview?.MsgType}, Content={preview?.Content}");
                    ResetTimer();
                }
                catch
                {
                    Console.WriteLine($"Received (unformatted): {receivedJson}");
                    ResetTimer();
                }

                // Deserialize the received JSON into a Message object
                Message? receivedMsg = JsonSerializer.Deserialize<Message>(receivedJson);
                if (receivedMsg == null)
                {
                    Console.WriteLine("Invalid message format");
                    continue;
                }

                // Handle message depending on the current state of the server protocol
                switch (currentStep)
                {
                    // TODO: [Receive and print Hello]
                    case ServerStep.AwaitHello:
                        // Handle Hello message from client and respond with Welcome
                        if (receivedMsg.MsgType == MessageType.Hello)
                        {
                            Console.WriteLine("Hello received from client.");
                            // TODO: [Send Welcome to the client]
                            Message welcome = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Welcome, Content = "Welcome from server" };
                            SendMessage(serverSocket, welcome, clientEP);
                            currentStep = ServerStep.AwaitLookup;
                        }
                        break;

                    // TODO: [Receive and print DNSLookup]
                    case ServerStep.AwaitLookup:
                        // Handle DNSLookup message, perform validation and respond accordingly
                        if (receivedMsg.MsgType == MessageType.DNSLookup)
                        {
                            Console.WriteLine("DNSLookup received from client.");
                            DNSRecord? requestedRecord = JsonSerializer.Deserialize<DNSRecord>(receivedMsg.Content!.ToString()!);

                            // TODO: [If not found or invalid Send Error]
                            // Validate that Type and Name fields are not empty or null
                            if (requestedRecord == null || string.IsNullOrWhiteSpace(requestedRecord.Name) || string.IsNullOrWhiteSpace(requestedRecord.Type))
                            {
                                Message error = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Incomplete DNSLookup" };
                                SendMessage(serverSocket, error, clientEP);
                                break;
                            }

                            // Validate the domain format using a regular expression
                            if (!IsValidDomain(requestedRecord.Name))
                            {
                                Message error = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Invalid domain format" };
                                SendMessage(serverSocket, error, clientEP);
                                break;
                            }

                            // Attempt to find a matching DNS record based on type and name
                            var foundRecord = dNSRecords?.FirstOrDefault(r =>
                                r.Type.Equals(requestedRecord.Type, StringComparison.OrdinalIgnoreCase) &&
                                (NormalizeDomain(r.Name).Equals(NormalizeDomain(requestedRecord.Name), StringComparison.OrdinalIgnoreCase))
                            );

                            // TODO: [If found Send DNSLookupReply containing the DNSRecord]
                            if (foundRecord != null)
                            {
                                // Valid DNS record found: send DNSLookupReply and start ACK wait
                                Message reply = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.DNSLookupReply, Content = foundRecord };
                                lastSentReply = reply;
                                retryCount = 0;
                                expectingAck = true;
                                SendMessage(serverSocket, lastSentReply, clientEP);
                                currentStep = ServerStep.AwaitAck;
                            }
                            else
                            {
                                // No matching DNS record found
                                Message notFound = new Message { MsgId = receivedMsg.MsgId, MsgType = MessageType.Error, Content = "Domain not found" };
                                SendMessage(serverSocket, notFound, clientEP);
                            }
                        }
                        break;

                    // TODO: [Receive Ack about correct DNSLookupReply from the client]
                    case ServerStep.AwaitAck:
                        // Handle incoming ACK message and prepare for next lookup
                        if (receivedMsg.MsgType == MessageType.Ack)
                        {
                            Console.WriteLine($"ACK received for MsgId {receivedMsg.Content}");
                            retryCount = 0;
                            lastSentReply = null;
                            expectingAck = false;
                            currentStep = ServerStep.AwaitLookup;
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

    // TODO: [Serialize and send a message using SendTo()]
    // Helper function to serialize and send a message to the client
    static void SendMessage(Socket socket, Message message, EndPoint client)
    {
        string json = JsonSerializer.Serialize(message);
        byte[] data = Encoding.UTF8.GetBytes(json);
        socket.SendTo(data, client);
        Console.WriteLine($"Sent: {json}");
    }

    // TODO: [Reset timer countdown when valid message is received]
    // Resets the timeout timer to avoid premature session end
    static void ResetTimer()
    {
        countdown = timeoutDuration;
        inactivityTimer.Stop();
        inactivityTimer.Start();
        Console.WriteLine("Timer reset to 10 seconds");
    }

    // TODO: [Verify syntax/structure of the domain name]
    // Validates a domain name format using regex
    static bool IsValidDomain(string domain)
    {
        var regex = new System.Text.RegularExpressions.Regex(@"^(www\.)?([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}$");
        return regex.IsMatch(domain);
    }

    // TODO: [Normalize domain names for consistent comparison]
    // Strips 'www.' from the beginning of the domain name for normalization
    static string NormalizeDomain(string domain)
    {
        if (domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            return domain.Substring(4);
        return domain;
    }
}
