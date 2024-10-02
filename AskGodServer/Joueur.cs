using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;


namespace AskGodServer
{
    internal class Joueur
    {
        private string nom;
        private string motDePasse;
        private Socket socketDuJoueur;
        private List<string> historique;
        private int nbFlagReussi = 0;

        public Joueur(string nom, string motDePasse, Socket socketDuJoueur)
        {
            this.nom = nom;
            this.motDePasse = motDePasse;
            this.socketDuJoueur = socketDuJoueur;
            historique = new List<string>();
        }

        public string getNom()
        {
            return nom;
        }

        public string getMotDePasse()
        {
            return motDePasse;
        }

        public Socket getSocketDuJoueur()
        {
            return socketDuJoueur;
        }

        public int getNbFlagReussi() { return nbFlagReussi; }

        public void IncrementerNbFlagReussi()
        {
            nbFlagReussi++;
        }


        public void addFlagHistorique(string flag) { historique.Add(flag); }

        public List<string> getHistorique() { return historique; }
    }
}
