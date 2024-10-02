using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using UtilCS.Network;
using static System.Console;
using Heyes;


namespace AskGodClient
{
    internal class ClientAskGod
    {
        const string NomProjet = "Client AskGod";
        private static IPEndPoint remoteEndpoint;
        const int Port = 1234;
        static TcpClient tcpClient;
        static UdpClient udpClient;

        static void Main()
        {
            Title = NomProjet;
            WriteLine("Client AskGod");
            WriteLine("Veuillez spécifier le IP:port avec l'option -s ou appuyer sur entrer pour se connecter au port 1234");
            string option = ReadLine();
            if (option.Trim() != "")
            {
                for (; ; )
                {
                    if (RespecteSynopsisOption(option, out string adresse, out int port))
                    {
                        udpClient = new UdpClient(port);
                        tcpClient = NetworkUtils.PréparerSocketTCPConnecté(adresse, port);
                        break;
                    }
                    else
                    {
                        WriteLine("FAIL commande invalide");
                        break;
                    }
                }
            }
            else
            {
                IPEndPoint dequi = null;
                udpClient = new UdpClient(Port);
                udpClient.RecevoirMessage(ref dequi, out string message);
                WriteLine(message);

                tcpClient = NetworkUtils.PréparerSocketTCPConnecté(dequi.Address.ToString(), Port);
            }

            if (tcpClient == null)
            {
                WriteLine("Impossible de se connecer au serveur");
                return;
            }
            WriteLine("Connexion réussi");
            tcpClient.RecevoirMessage(out string messageServeur);
            WriteLine(messageServeur);

            for (; ; )
            {
                WriteLine("Indiquez un pseudo et un mot de passe");
                string infoJoueur = ReadLine();
                tcpClient.EnvoyerMessage("REG | " + infoJoueur);
                string reponse;
                tcpClient.RecevoirMessage(out reponse);

                string code = reponse.Trim().Split('|')[0].Trim();
                string message = reponse.Trim().Split('|')[1];

                if (code == "200")
                {
                    WriteLine(message);
                    break;
                }
                else
                {
                    WriteLine("FAIL veuillez indiquer un pseudo et mot de passe valide");
                }
            }

            for (; ; )
            {
                WriteLine("Indiquez une team et sont mot de passe");
                string infoJoueur = ReadLine();
                tcpClient.EnvoyerMessage("REGTEAM | " + infoJoueur);
                string reponse;
                tcpClient.RecevoirMessage(out reponse);

                string code = reponse.Trim().Split('|')[0].Trim();
                string message = reponse.Trim().Split('|')[1];

                if (code == "200")
                {
                    WriteLine(message);
                    break;
                }
                else
                {
                    WriteLine("FAIL veuillez indiquer un nom d'équipe");
                }
            }

            WriteLine("Vous basculez en mode 'interactif' et pouvez maintenant faire les commandes suivantes :\nSTATUS, HISTORY, SCOREBOARD et SUBMIT | (flag).");
            string messageUser = ReadLine();
            tcpClient.EnvoyerMessage(messageUser);

            for (; ; )
            {
                if (tcpClient.RecevoirLigne(out messageServeur))
                {
                    if (messageServeur.StartsWith("245") || messageServeur.StartsWith("246") || messageServeur.StartsWith("247"))
                    {
                        WriteLine(messageServeur);
                        for (; ; )
                        {
                            tcpClient.RecevoirLigne(out messageServeur);
                            /***/
                            if (messageServeur == "") break;
                            /***/
                            WriteLine(messageServeur);

                        }
                    }
                    else
                    {
                        if (messageServeur.StartsWith("200") || messageServeur.StartsWith("401"))
                        {
                            WriteLine(messageServeur);
                        }
                        else
                        {
                            WriteLine("400 Erreur d'envoi");
                            WriteLine("Vos commandes sont : STATUS, HISTORY, SCOREBOARD et SUBMIT.");
                            WriteLine("Pour SUBMIT vous devez préciser le flag ainsi :");
                            WriteLine("SUBMIT | CTF-...");
                        }
                    }

                    messageUser = ReadLine();
                    tcpClient.EnvoyerMessage(messageUser);
                }


                if (!udpClient.RecevoirMessage(ref remoteEndpoint, out string messageReçu))
                {
                    WriteLine("Erreur de réception.");
                    break;
                }
            }
        }

        static bool RespecteSynopsisOption(string option, out string adresse, out int port)
        {
            string[] args = option.Trim().Split(' ');

            adresse = null;
            port = 0;

            GetOpt getOpt = new GetOpt(args);
            getOpt.SetOpts(["s="]);

            try
            {
                getOpt.Parse();
            }
            catch (ArgumentException ex)
            {
                WriteLine("FAIL Erreur de traitement de paramètres.\n" +
                    "Message reçu de la librairie getopt : " + ex.Message);
                return false;
            }

            if (getOpt.IsDefined("s"))
            {
                adresse = getOpt.GetOptionArg("s").Split(':')[0];
                port = Convert.ToInt32(getOpt.GetOptionArg("s").Split(":")[1]);
                return true;
            }

            return false;
        }
    }
}
