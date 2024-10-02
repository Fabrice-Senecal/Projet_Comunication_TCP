using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AskGodServer
{
    internal class Equipe
    {
        private string nom;
        private string motDePasse;
        private List<Joueur> joueurs;
        private int score = 0;
        public Equipe(string nom, string motDePasse)
        {
            this.nom = nom;
            this.motDePasse = motDePasse;
            joueurs = new List<Joueur>();
        }

        public void AugmenterScore(int score)
        {
            this.score += score;
        }

        public int getScore() { return score; }

        public string getNom()
        {
            return nom;
        }

        public string getMotDePasse()
        {
            return motDePasse;
        }

        public void AjouterJoueur(Joueur j)
        {
            joueurs.Add(j);
        }

        public List<Joueur> getJoueurs()
        {
            return joueurs;
        }
    }
}
