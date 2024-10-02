using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AskGodServer
{
    internal class Challenge
    {
        private string nom;
        private string flag;
        private int score;

        public Challenge(string nom, string flag, int score)
        {
            this.nom = nom;
            this.flag = flag;
            this.score = score;
        }

        public string getNom() { return nom; }

        public string getFlag() { return flag; }

        public int getScore() { return score; }
    }
}
