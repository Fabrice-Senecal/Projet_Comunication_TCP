using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Net;
using UtilCS.Network;
using static System.Console;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.NetworkInformation;

namespace AskGodServer
{
    class Broadcaster
    {
        List<UdpClient> socketsPourInterfaces = new List<UdpClient>(); // Membre d'une classe
                                                                       // CTOR

        public static Broadcaster CréerBroadcaster(int port)
        {

            return new Broadcaster(port);
        }

        Broadcaster(int portDistant)
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()
            .ToList().FindAll(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var ip = nic.GetIPProperties().UnicastAddresses
                .First(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ip != null)
                {
                    UdpClient u = new UdpClient(new IPEndPoint(ip.Address, 0));
                    u.ConnecterUDPPourDiffusion(portDistant);
                    socketsPourInterfaces.Add(u);
                }
            }

        }

        public void DiffuserMessage(string message)
        {
            // TODO DONE : envoyer le message sur chaque client UDP
            foreach (var socketInterface in socketsPourInterfaces)
                socketInterface.EnvoyerMessage(message);

        }

    }
}
