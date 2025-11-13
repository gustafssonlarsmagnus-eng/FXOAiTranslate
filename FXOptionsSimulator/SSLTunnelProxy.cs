using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace FXOptionsSimulator
{
    public class SSLTunnelProxy
    {
        private TcpListener _localListener;
        private readonly string _remoteHost;
        private readonly int _remotePort;
        private readonly int _localPort;
        private bool _isRunning;

        public SSLTunnelProxy(string remoteHost, int remotePort, int localPort = 9443)
        {
            _remoteHost = remoteHost;
            _remotePort = remotePort;
            _localPort = localPort;
        }

        public void Start()
        {
            _localListener = new TcpListener(IPAddress.Loopback, _localPort);
            _localListener.Start();
            _isRunning = true;

            Console.WriteLine($"[SSL Proxy] Listening on localhost:{_localPort}");
            Console.WriteLine($"[SSL Proxy] Forwarding to {_remoteHost}:{_remotePort}");

            Task.Run(() => AcceptClients());
        }

        private async Task AcceptClients()
        {
            while (_isRunning)
            {
                try
                {
                    var localClient = await _localListener.AcceptTcpClientAsync();
                    Console.WriteLine($"[SSL Proxy] Client connected");
                    _ = Task.Run(() => HandleClient(localClient));
                }
                catch (Exception ex)
                {
                    if (_isRunning) // Only log if not shutting down
                    {
                        Console.WriteLine($"[SSL Proxy] Error accepting client: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleClient(TcpClient localClient)
        {
            TcpClient remoteClient = null;
            SslStream sslStream = null;

            try
            {
                // Connect to remote server with SSL
                remoteClient = new TcpClient();
                await remoteClient.ConnectAsync(_remoteHost, _remotePort);

                sslStream = new SslStream(
                    remoteClient.GetStream(),
                    false,
                    new RemoteCertificateValidationCallback(ValidateServerCertificate),
                    null
                );

                await sslStream.AuthenticateAsClientAsync(_remoteHost);
                Console.WriteLine($"[SSL Proxy] SSL connection established to {_remoteHost}");

                // Get streams
                var localStream = localClient.GetStream();

                // Bi-directional relay
                var localToRemote = RelayData(localStream, sslStream, "Local->Remote");
                var remoteToLocal = RelayData(sslStream, localStream, "Remote->Local");

                await Task.WhenAny(localToRemote, remoteToLocal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSL Proxy] Error: {ex.Message}");
            }
            finally
            {
                sslStream?.Close();
                remoteClient?.Close();
                localClient?.Close();
                Console.WriteLine($"[SSL Proxy] Client disconnected");
            }
        }

        private async Task RelayData(System.IO.Stream source, System.IO.Stream destination, string direction)
        {
            byte[] buffer = new byte[8192];
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead);
                    await destination.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                // Connection closed or error - this is normal
            }
        }

        private bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // Accept all certificates (for UAT testing)
            // In production, validate properly!
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine($"[SSL Proxy] Certificate warning: {sslPolicyErrors}");
            return true; // Accept anyway for UAT
        }

        public void Stop()
        {
            _isRunning = false;
            _localListener?.Stop();
            Console.WriteLine($"[SSL Proxy] Stopped");
        }
    }
}