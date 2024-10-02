using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UtilCS.Network;
using static System.Console;


namespace AskGodServer
{
    internal class AskGodServeur
    {
        static ConcurrentBag<Equipe> listeEquipe = new ConcurrentBag<Equipe>();

        static ConcurrentBag<Joueur> listeJoueurs = new ConcurrentBag<Joueur>();

        static ConcurrentBag<Challenge> listeChallenge = new ConcurrentBag<Challenge>();

        static void Main()
        {

            Title = "Serveur AskGod";

            new Thread(Diffuser).Start();

            var socketMaitre = NetworkUtils.ÉcouterTCP(1234);

            for (; ; ) // forever listening or die trying
            {
                Socket socketClient = socketMaitre.Accepter();
                string ipDistant = socketClient.RemoteEndPoint.ToString();
                WriteLine("Un client connecté de : " + ipDistant);
                new Thread(ServirClient).Start(socketClient);
            }
        }

        static void ServirClient(object obj)
        {
            Joueur joueurActuel = null;
            bool objIsSocket = obj is Socket;
            if (!objIsSocket)
                throw new InvalidOperationException("SVP me passer un thread seulement.");
            Socket socketClient = (Socket)obj;
            socketClient.EnvoyerMessage("201 Bienvenue vous avez été accepté dans le serveur ASK GOD number 1.");
            genererChallenges();
            
            for (; ; )
            {
                string message;
                string messageClient = null;
                if (!socketClient.RecevoirMessage(out message))
                {
                    break;
                }

                string code = message.Trim().Split('|')[0].ToUpper().Trim();
                if (message.Trim().Split('|').Length > 1)
                {
                    messageClient = message.Trim().Split('|')[1];
                }
                switch (code)
                {
                    case "REG":
                        TraiterReg(messageClient, socketClient, out joueurActuel);
                        break;
                    case "REGTEAM":
                        TraiterRegteam(messageClient, socketClient, joueurActuel);
                        break;
                    case "STATUS":
                        TraiterStatus(socketClient);
                        break;
                    case "HISTORY":
                        TraiterHistory(joueurActuel, socketClient);
                        break;
                    case "SCOREBOARD":
                        TraiterScoreBoard(socketClient);
                        break;
                    case "SUBMIT":
                        TraiterSubmit(messageClient, joueurActuel, socketClient);
                        break;
                    case "FLAG":
                        TraiterFlag(socketClient);
                        break;
                    default:
                        socketClient.EnvoyerMessage("400 | Erreur");
                        break;
                }
            }
            WriteLine("Le client " + socketClient.RemoteEndPoint + " s'est déconnecté. On termine le thread.");

            socketClient.Close();
        }

        private static void Diffuser()
        {
            Broadcaster broadcaster = Broadcaster.CréerBroadcaster(1234);

            for (; ; )
            {
                broadcaster.DiffuserMessage("Serveur_askgod number 1");
                Thread.Sleep(2000);
            }
        }

        private static void genererChallenges()
        {
            Challenge challenge1 = new Challenge("[Binary Breakthrough]", "CTF-B1n@ryBr34k!", 20);
            listeChallenge.Add(challenge1);
            Challenge challenge2 = new Challenge("[Cipher Conundrum]", "CTF-C1ph3rC0nunDruM", 10);
            listeChallenge.Add(challenge2);
            Challenge challenge3 = new Challenge("[Web Wonders]", "CTF-W3bW0nd3rsCTF", 10);
            listeChallenge.Add(challenge3);
            Challenge challenge4 = new Challenge("[Forensic Frenzy]", "CTF-F0r3ns1cFr3nzY!", 30);
            listeChallenge.Add(challenge4);
            Challenge challenge5 = new Challenge("[Crypto Quest]", "CTF-CrYpt0Qu35t!", 10);
            listeChallenge.Add(challenge5);
            Challenge challenge6 = new Challenge("[Networking Nemesis]", "CTF-N3twork1ngN3m3sis", 50);
            listeChallenge.Add(challenge6);
            Challenge challenge7 = new Challenge("[Steganography Saga]", "CTF-St3g4n0gr4phY", 50);
            listeChallenge.Add(challenge7);
            Challenge challenge8 = new Challenge("[Reverse Engineering Riddle]", "CTF-R3v3rs3Eng1ne3r", 10);
            listeChallenge.Add(challenge8);
            Challenge challenge9 = new Challenge("[Exploit Expedition]", "CTF-Explo1tExped!t10n", 30);
            listeChallenge.Add(challenge9);
            Challenge challenge10 = new Challenge("[Mobile Mysteries]", "CTF-M0b1leMy5ter1es", 20);
            listeChallenge.Add(challenge10);
            Challenge challenge11 = new Challenge("[Pwnable Puzzle]", "CTF-Pwn4blePuzzl3!", 20);
            listeChallenge.Add(challenge11);
            Challenge challenge12 = new Challenge("[SQL Injection Safari]", "CTF-SQL1nj3ctionSafar1!", 10);
            listeChallenge.Add(challenge12);

        }

        private static void NotifierJoueurs(Equipe e)
        {
            foreach (Joueur j in listeJoueurs)
            {
                j.getSocketDuJoueur().EnvoyerMessage($"La team {e.getNom} vine de remporter un flag");
            }
        }

        private static int FlagEstValide(string flag)
        {
            int score = 0;
            foreach (Challenge challenge in listeChallenge)
            {
                if (challenge.getFlag() == flag)
                {
                    score = challenge.getScore();
                }
            }
            return score;
        }

        private static Joueur JoueurDejaEnregistre(string pseudo)
        {
            foreach (Joueur jo in listeJoueurs)
            {
                if (jo.getNom() == pseudo)
                    return jo;
            }

            return null;
        }

        private static Equipe GetJoueurEquipe(Joueur jo)
        {
            Equipe equipe = null;
            foreach (Equipe e in listeEquipe)
            {
                foreach (Joueur j in e.getJoueurs())
                {
                    if (jo.getNom() == j.getNom())
                    {
                        equipe = e; break;
                    }
                }
            }
            return equipe;
        }

        private static Equipe EquipeDejaEnregistre(string nom)
        {
            foreach (Equipe e in listeEquipe)
            {
                if (e.getNom() == nom)
                    return e;
            }

            return null;
        }

        private static void TraiterRegteam(string message, Socket socket, Joueur joueurActuel)
        {
            string nom;
            string password = null;
            Equipe equipe;
            if (!string.IsNullOrEmpty(message))
            {
                nom = message.Trim().Split(" ")[0];
                if (message.Trim().Split(" ").Length > 1)
                {
                    password = message.Trim().Split(" ")[1];
                }
                else
                {
                    socket.EnvoyerMessage("400 | FAIL MAUVAIS ENVOI, il vous manque un mot de passe");
                    return;
                }
            }
            else
            {
                socket.EnvoyerMessage("400 | FAIL MAUVAIS ENVOI");
                return;
            }

            if (nom.Trim().Length > 0)
            {
                if (EquipeDejaEnregistre(nom) != null)
                {
                    equipe = EquipeDejaEnregistre(nom);
                    if (equipe.getMotDePasse() != password)
                    {
                        socket.EnvoyerMessage("400 | FAIL MAUVAIS MOT DE PASSE pour " + equipe.getNom());
                        return;
                    }
                    else
                    {
                        equipe.AjouterJoueur(joueurActuel);
                        socket.EnvoyerMessage("200 | OK vous etes ajouté à " + equipe.getNom());
                    }
                }
                else
                {
                    equipe = new Equipe(nom, password);
                    equipe.AjouterJoueur(joueurActuel);
                    listeEquipe.Add(equipe);
                    socket.EnvoyerMessage("200 | Nouvelle équipe créer : " + equipe.getNom());
                }
            }
            else
            {
                socket.EnvoyerMessage("400 | FAIL veuillez indiquer un nom d'équipe");
                return;
            }
        }
        private static void TraiterReg(string message, Socket socket, out Joueur joueurActuel)
        {
            joueurActuel = null;
            string pseudo;
            string password = null;
            if (!string.IsNullOrEmpty(message))
            {
                pseudo = message.Trim().Split(" ")[0];
                if (message.Trim().Split(" ").Length > 1)
                {
                    password = message.Trim().Split(" ")[1];
                }
                else
                {
                    socket.EnvoyerMessage("400 | FAIL MAUVAIS ENVOI, il vous manque un mot de passe");
                    return;
                }
            }
            else
            {
                socket.EnvoyerMessage("400 | FAIL MAUVAIS ENVOI");
                return;
            }


            if (pseudo.Trim().Length > 0)
            {
                if (JoueurDejaEnregistre(pseudo) != null)
                {
                    joueurActuel = JoueurDejaEnregistre(pseudo);
                    if (joueurActuel.getMotDePasse() != password)
                    {
                        socket.EnvoyerMessage("400 | FAIL MAUVAIS MOT DE PASSE");
                        joueurActuel = null;
                        return;
                    }
                    else
                    {
                        socket.EnvoyerMessage("200 | OK vous etes connecté");
                    }
                }
                else
                {
                    Joueur joueur = new Joueur(pseudo, password, socket);
                    listeJoueurs.Add(joueur);
                    joueurActuel = joueur;
                    socket.EnvoyerMessage("200 | Nouveau joueur créer !");
                }
            }
            else
            {
                socket.EnvoyerMessage("400 | FAIL veuillez indiquer un pseudo");
                return;
            }
        }

        private static void TraiterStatus(Socket socket)
        {
            socket.EnvoyerMessage("247 | Nombre de joueurs enregistré : " + listeJoueurs.Count().ToString());
            socket.EnvoyerMessage("Nombre d'équipes enregistré : " + listeEquipe.Count().ToString());
            socket.EnvoyerMessage("Nombre de défis : " + listeChallenge.Count().ToString());
            socket.EnvoyerMessage("");
        }

        private static void TraiterHistory(Joueur joueur, Socket socket)
        {
            socket.EnvoyerMessage("245 | Historique pour : " + joueur.getNom());
            if (joueur.getHistorique().Count() > 0)
            {
                foreach (string flag in joueur.getHistorique())
                {
                    socket.EnvoyerMessage($"{flag}");
                }
                socket.EnvoyerMessage("Pour un total de " + joueur.getHistorique().Count + " flags.");
                double pourcentage = Convert.ToDouble(joueur.getNbFlagReussi() / joueur.getHistorique().Count()) * 100;
                socket.EnvoyerMessage(pourcentage + "% des flags envoyer on été reussi!");
            }
            else
            {
                socket.EnvoyerMessage("Aucun flag n'a été capturé.");
            }
            socket.EnvoyerMessage("");
        }

        private static void TraiterScoreBoard(Socket socket)
        {
            socket.EnvoyerMessage("246");
            socket.EnvoyerMessage("---SCORE Équipes ---");

            foreach (Equipe equipe in listeEquipe)
            {
                socket.EnvoyerMessage("Équipe " + equipe.getNom() + " score : " + equipe.getScore());
            }

            socket.EnvoyerMessage("");
        }

        private static void TraiterSubmit(string flag, Joueur joueur, Socket socket)
        {
            int score;
            if (flag != null)
            {
                if ((score = FlagEstValide(flag.Trim())) > 0)
                {
                    joueur.addFlagHistorique(flag);
                    joueur.IncrementerNbFlagReussi();
                    Equipe equipe = GetJoueurEquipe(joueur);
                    equipe.AugmenterScore(score);
                    socket.EnvoyerMessage($"200 | {flag} valide, avec un score de : {score}");
                    NotifierJoueurs(equipe);
                }
                else
                {
                    joueur.addFlagHistorique(flag);
                    socket.EnvoyerMessage($"401 | {flag} invalide");
                    return;
                }
            }
            else
            {
                socket.EnvoyerMessage("400 | FAIL MAUVAIS ENVOI");
            }

        }

        private static void TraiterFlag(Socket socket)
        {
            socket.EnvoyerMessage("245");
            foreach (Challenge c in listeChallenge)
            {
                socket.EnvoyerMessage(c.getFlag());
            }
            socket.EnvoyerMessage("");
        }
    }
}
