/*
Nom : NetworkUtils
Auteur : PMC 2016-2018, 2022-2023 CC-BY-SA
Description : 

# Cette librairie fournie 
  - des méthodes d'extension pour les types TcpClient (et son stream) ainsi que pour UdpClient (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/extension-methods)
	    - TcpClient encapsule un socket et permet de l'utiliser comme un stream ce qui confère des avantages au niveau de l'abstraction
	    - UdpClient encapsule aussi un socket, mais avec un RemoteEndPoint qui n'est jamais réellement connecté 
	    - Pour des nouveaux projets, je recommande de manipuler les abstractions TcpClient/NetworkStream et UdpClient directement,
	      donc sans manipuler les sockets directement. 
          L'exception sera pour faire des serveurs avec l'API Select() où on préférera les sockets directement.
          Pour les serveurs multithreads, on peut accepter les nouveaux clients avec Socket ou avec TcpClient, les deux fonctionneront.
      
  - des méthodes d'extension pour les sockets sont fournies à titre patrimonial
  - des méthodes de fabrique pour des TcpClient et UdpClient
  - des validations et de la gestion d'exceptions, ce qui est inévitable en manipulant ces classes (fin de connexion, timeout, etc.)
  - des méthodes simplifiées pour certains protocoles text-based (HTTP, FTP, POP/SMTP) et la gestion des codes de réponses
     e.g RecevoirJusquaLigneVide pour HTTP, RecevoirJusquaPoint pour POP
  
      

# Certaine méthodes affichent des messages d'erreurs dans stderr, que vous pouvez rediriger ailleurs
	si vous ne voulez pas que ces messages s'affichent dans la console (ils s'afficheront dans la console par défaut)
    - En WinForms ou autre framework avec UI, vous ne les verrez pas nécessairement,
      sauf si vous redirigez stdout/stderr dans un textbox multiligne ou autre contrôle..
    - 


# Les méthodes d'envoi et de réception utilisent la propriété Encodage pour encoder/décoder.
  J'utilise par défaut l'encodage par défaut (et non ASCII). 
	Si vous envoyez des caractères non représentables, ils seront transformés en '?'
  Changez pour ASCII au besoin. ASCII à l'avantage d'être 99.9% portable et utile si vous ne contrôlez pas le client et le serveur.
  Changez pour UTF8 au besoin. UTF8 a un cout (overhead) de commmunication réseau. Négligeable, sauf à grand échelle. On peut l'utiliser si on contrôle le client et le serveur.
	J'aurais aussi pu lancer une exception pour vour prévenir si vous "perdez" des caractères mais ca aurait aussi été couteux en traitement.
	
# J'ai choisi de ne lancer aucune exception dans la librairie.
	C'est discutable, mais ca va dans la même idée que votre utilitaire Console développé en Prog.1.
	Je capte donc certaines exception de la librairie. 
	Certaines sont normales et habituelles, je journalise donc l'erreur et vous retourne un booléen.
	Certaines sont inhabituelles et je les relancent donc avec un 'throw' après avoir journalisé avec AfficherErreur.
  Si vous recevez des exceptions de la librairie, la solution est probablement de corriger votre code de votre coté plutot que 
	de capter l'exception.


# Dépendances
	Cette librairie n'a aucune dépendance spécifique. C'est voulu.

# Null-aware
	La librairie n'est pas null-aware. Désactiver le null aware context dans vos projets si vous ne voulez pas les warnings.

# Méthodes avec timeout
	Ajout de quelques méthodes qui supportent les timeout. Il manque possiblement quelques surcharges. 
  À noter que même si la librairie ne supportait pas les timeout, ca serait facile pour vous de l'implémenter
  en manipulant les propriétés ReceiveTimeout et SendTimeout des sockets. Ensuite, vous devez simplement capter 
  les socketException avec SocketError.TimedOut. Bref, la librairie possède des méthodes qui le font automatiquement
  pour vous et retourne un booléen 'timeout'. Vous n'avez donc pas à capter l'exception.

 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
//using static UtilCS.ConsoleUtils.ConsoleUtils; // retiré 2022-01-12 pour éliminer la dépendance
using static System.Console;

namespace UtilCS.Network
{
	public static class NetworkUtils
	{
		// Constantes : ces 'constantes' sont implémentées sous forme de public static.
		//		Cela permet de les modifier au runtime (utile pour le debug notamment). 
		//	  Cela permet aussi de les setter depuis vos programmes sans modifier l'utilitaire.
		//	  Vous recevrez normalement un message CA2211 indiquant qu'il n'est pas safe de 'setter' les valeurs 
		//    dans un environnement multithread, ce qui n'est pas notre cas.
		//    Vous pouvez ignorer ce message mais sachez qu'on utiliserait normalement un mutex/lock pour chaque propriété 'set'.

		// J'ai laissé cette propriété configurable pour mes démonstrations mais comme expliqué au cours, la solution n'est jamais d'augmenter la taille de la file pour pallier à des problèmes réseaux
		public static int FileAttenteTCP = 5; // standard ... si le serveur est bien configuré il y aura jamais de client qui recevra un deny

		// Ce mutex (null par défaut) est à setter si nécessaire pour obtenir une synchro à la Console dans un environnement multiprocessus. 
		// Cela permet d'éviter que les messages soient "mêlés".
		public static Mutex MutexSynchroConsole;

		public static string FinDeLigne = Environment.NewLine; // configurable mais supporte seulement \n ou \r\n

		public static bool Verbose = false; // utilisé pour afficher les communications recues/envoyées dans stdout (par défaut)

		public static bool VerboseErreur = true; // utilisé pour afficher/journaliser les erreurs dans stderr

		public static Encoding Encodage = Encoding.Default; // configurable mais on n'y touche pas à moins d'avoir le contrôle total sur le serveur et tous les clients

		// Couleur utilisée pour les messages d'information
		private static ConsoleColor couleurInfo = ConsoleColor.Cyan;
		public static ConsoleColor CouleurInfo
		{
			get
			{
				return couleurInfo;
			}

			set
			{
				if (value == ConsoleColor.Red)
					throw new ArgumentException("Veuillez ne pas utiliser rouge SVP.");

				couleurInfo = value;
			}
		}

		public static string SentinelleInvite = ":"; // sert pour ignorer des caractères recus et à reconnaitre le moment où on doit envoyer
		public static string SentinelleInfo = ":"; // sert pour ignorer certains caractères et à reconnaitre le moment ou le message qu'on recherche commence
		public static string PréfixeRéception = "<<<< ";
		public static string PréfixeEnvoi = ">>>>> ";
		public static string PréfixeErreur = "*** ";
		const string Message1005x = "Déconnexion abrupte {0}. Cela veut dire que si vous tentiez d'envoyer, le socket n'est plus disponible car fermé par le serveur via un TCP {1} et non un TCP {2}. Si vous tentiez de recevoir, c'est le même raisonnement qui s'applique. Dans tous les cas, ce socket n'est plus utilisable.";
		static readonly string Message10053 = string.Format(Message1005x, 10053, "RST", "FIN");
		static readonly string Message10054 = string.Format(Message1005x, 10054, "FIN", "RST");
		const string Message11001HostNotFound = "Erreur de connexion. Vérifiez votre DNS.";
		const string MessageInvalidOperationSocketFermé = "Vous tentez d'envoyer ou recevoir sur une connexion fermée. Corrigez votre code. ";


		//private static object baton = new object();


		/// <summary>
		/// Fonction bloquante qui acceptera un nouveau client. 
		/// Si vous avez vérifié en amont que le socket est "prêt", par exemple avec Select() ou autre 
		///   mécanisme, la fonction ne bloquera pas et retournera le socket "connecté" instantanément.
		///   
		/// Cette fonction ne fait pas grand chose de plus que le Accept() de la classe Socket 
		///   mais je l'ai ajouté par souci de modularité, d'uniformité et d'extensibilité si 
		///   jamais je veux ajouter des validations ou autre.
		/// </summary>
		/// <param name="socket">Un socket maitre (en mode listen seulement, pas de remote endpoint)</param>
		/// <returns>Un socket client connecté</returns>
		public static Socket Accepter(this Socket socketMaitre)
		{
			Socket socketClient;
			try
			{
				socketClient = socketMaitre.Accept();

				if (Verbose)
					AfficherInfo("Client accepté :\n\tEndpoint distant : " + socketClient.RemoteEndPoint + "\n\t" + "Endpoint local : " + socketClient.LocalEndPoint);

			}
			catch (SocketException ex)
			{
				AfficherErreur("Erreur au moment d'accepter. Peu probable mais bon." +
					"Code erreur : " + ex.ErrorCode);
				throw;
			}

			return socketClient;
		}

		private static void AfficherCommunication(string message)
		{
			if (!Verbose) return;

			// Cette ligne est normalement la 1re du fichier qui donne un warning IDE0031 suggérant de remplacer par `MutexSynchroConsole?.WaitOne()`,
			//  ce que je ne ferai pas pas souci de rétro-compatibilité.
			// Vous avez deux options :
			// - ajouter ceci au GlobalSuppressions.cs : [assembly: SuppressMessage("Style", "IDE0031:Use null propagation", Justification = "<Pending>", Scope = "module")]
			// - ajouter ceci au .editorconfig : dotnet_analyzer_diagnostic.severity = none
			// La 2e option va fonctionner seulement si le fichier NetworkUtils.cs est sous l'arborescence de votre projet. 
			if (MutexSynchroConsole != null) MutexSynchroConsole.WaitOne();

			ConsoleColor ancienneCouleur = ForegroundColor;
			ForegroundColor = ConsoleColor.Green;
			WriteLine(message);
			ForegroundColor = ancienneCouleur;
			if (MutexSynchroConsole != null) MutexSynchroConsole.ReleaseMutex();


		}

		private static void AfficherEnvoi(string message) => AfficherCommunication(PréfixeEnvoi + message);



		/// <summary>
		/// Voir <seealso cref="AfficherErreur(string, object[])"/>
		/// </summary>
		private static void AfficherErreur(string erreur)
		{
			if (!VerboseErreur) return;

			// Décommentez cette ligne si vous voulez tester de façon automatique si votre synchro est bien faite !
			//if (!(Console.ForegroundColor == ConsoleColor.Yellow || Console.ForegroundColor == ConsoleColor.White))
			//  throw new Exception("console pas la bonne couleur !");

			if (MutexSynchroConsole != null) MutexSynchroConsole.WaitOne(); // Down

			ConsoleColor ancienneCouleur = ForegroundColor;
			ForegroundColor = ConsoleColor.Red;
			Error.WriteLine(PréfixeErreur + erreur);
			ForegroundColor = ancienneCouleur;

			if (MutexSynchroConsole != null) MutexSynchroConsole.ReleaseMutex(); // Up

		}

		/// <summary>
		/// Méthode qui permet d'afficher un message dans la couleur configurée pour les erreurs (rouge).
		/// Un mutex de synchro à la console sera utilisé si vous l'avez configuré.
		/// Normalement on mettrait cette fonction dans un utilitaire ailleurs, style ConsoleUtils comme en Prog.1.
		/// Il est mit ici pour rendre le fichier sans aucune dépendance et être intégré facilement à vos projets.
		/// </summary>
		public static void AfficherErreur(String format, params object[] arg0) => AfficherErreur(String.Format(format, arg0));



		/// <summary>
		/// Voir <seealso cref="AfficherInfo(string, object[])"/>
		/// </summary>
		/// <param name="info"></param>
		public static void AfficherInfo(string info)
		{
			// Décommentez cette ligne si vous voulez tester de façon automatique si votre synchro est bien faite ! Voir diapo #41 Présentation NetworkUtils et serveur concurrent
			//if (!(ForegroundColor == ConsoleColor.Yellow || ForegroundColor == ConsoleColor.White))
			//  throw new Exception("console pas la bonne couleur !");

			if (MutexSynchroConsole != null) MutexSynchroConsole.WaitOne(); // Down

			ConsoleColor ancienneCouleur = ForegroundColor;
			// ?
			// sans mutex configuré, peut être interrompu ici et causer un problème dans un environnement multithread ou multiprocessus
			ForegroundColor = CouleurInfo;
			// ou ici
			WriteLine(info);
			// ou ici, vous aurez compris que ca peut être n'importe où
			ForegroundColor = ancienneCouleur;

			if (MutexSynchroConsole != null) MutexSynchroConsole.ReleaseMutex(); // Up

		}

		/// <summary>
		/// Affiche le message dans la couleur prédéfinie pour les messages informatifs.
		/// Utilise un mécanisme de synchro. Voir <seealso cref="AfficherErreur(string, object[])"/>
		/// </summary>
		public static void AfficherInfo(String format, params object[] arg0) => AfficherInfo(String.Format(format, arg0));

		/// <summary>
		/// Une réception est une sorte de communication. Cette méthode ajoute simplement le préfixe configuré.
		/// </summary>
		private static void AfficherRéception(string message) => AfficherCommunication(PréfixeRéception + message);

		/// <summary>
		/// Utilise Socket.Select, un API de bas niveau du système d'exploitation bloquant.
		/// 
		/// Retourne le premier socket prêt.
		/// </summary>
		/// <param name="listeComplèteSockets"></param>
		/// <returns></returns>
		public static Socket AttendrePremierSocketPrêt(this List<Socket> listeComplèteSockets)
		{
			return AttendreSocketsPrêts(listeComplèteSockets)[0]; // retourne tjrs une liste ayant au moins un élément (will block or die trying), aucun problème à hardcoder indice 0 ici !
		}

		/// <summary>
		/// Utilise Socket.Select, un API de bas niveau du système d'exploitation bloquant.
		/// La méthode bloque jusqu'à ce qu'un des sockets recoive une communication (prêt)
		/// À noter que aucune information n'est "recue" des sockets, 
		/// elle fait seulement retourner la liste des sockets prêts.
		/// Il faudra appeler Receive ou Accept nous-même selon le socket qui est prêt.
		/// </summary>
		/// <param name="listeComplèteSockets"></param>
		/// <returns></returns>
		public static List<Socket> AttendreSocketsPrêts(this List<Socket> listeComplèteSockets)
		{
			var listeSocketsPrêts = new List<Socket>(listeComplèteSockets);
			// C'est la méthode Select() qui fait la magie. 
			// Elle "interroge" les sockets sans bloquer sur un en particulier.
			// Elle bloque donc sur l'ensemble mais on pourrait aussi spécifier un timeout.
			Socket.Select(listeSocketsPrêts, null, null, -1); // on attend pour toujours, pas de timeout. J'assume que vous allez exécuter ce code dans un thread dédié.
			return listeSocketsPrêts;

		}



		//public static List<TcpClient> AttendreTcpClientPrêts(this List<TcpClient> listeComplèteTcpClient)
		//{
		//  List<TcpClient> listeTcpClientPrêts = new List<TcpClient>(listeComplèteTcpClient);
		//  // C'est la méthode Select() qui fait la magie. 
		//  // Elle "interroge" les sockets sans bloque sur un en particulier.
		//  // Elle bloque donc sur l'ensemble mais on pourrait spécifier un timeout.

		//  Socket.Select(listeTcpClientPrêts, null, null, -1); // on attend pour toujours, pas de timeout. J'assume que vous allez exécuter ce code dans un thread dédié.
		//  return listeTcpClientPrêts;

		//}

		public static bool ConnecterTCP(this TcpClient tcpClient, string host, int portDistant)
		{
			if (tcpClient == null) throw new InvalidOperationException("Veuillez instancier votre TcpClient."); // en dehors du try pour éviter qu'il soit attrapé par le catch fallback

			try
			{
				                          tcpClient.Connect(host, portDistant);

				if (Verbose) AfficherInfo("Connexion réussie.");

				return true;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused) // 10061
			{
				// Refus du serveur, normalement cela arrive juste sur la même machine.
				AfficherErreur("Erreur de connexion. Refus du serveur local.");
				return false;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) // 10060
			{
				// Timeout, vérifiez le IP (cause probable mais ca peut être autre chose comme un problème de pare-feu, impossible de différencier de notre point de vue)
				AfficherErreur("Erreur de connexion. Vérifiez le IP ou le pare-feu.");
				return false;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound) // 11001
			{
				// Erreur 11001 host not found
				AfficherErreur(Message11001HostNotFound);
				return false;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.IsConnected)//10056
			{
				AfficherErreur("Le socket est déja connecté. Pas la peine de le reconnecter.");
				throw; // on veut que le programmeur corrige sa logique
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.NetworkUnreachable)
			{
				AfficherErreur("Vérifier le paramètre host (IP invalide).");
				return false; // J'ai privilégié return false au lieu de throw pour qu'on puisse profiter du fait que le TCPConnect peut s'occuper lui-même de valider si c'est un IP ou DNS valide. Toutefois, dans un monde idéal, on fait cette validation en amont.
			}
			catch (SocketException)
			{
				// problème réseau, journaliser et relancer selon vos besoins
				AfficherErreur("Erreur de connexion. Problème réseau inconnu.");
				throw;
			}
			catch (Exception)
			{
				// problème de plus bas niveau, journaliser et relancer
				AfficherErreur("Erreur de connexion. Problème de bas niveau. Vérifiez le stack trace.");
				throw;
			}


		}

		/// <summary>
		/// Surcharge pour IPEndPoint
		/// </summary>
		/// <param name="tcpClient">le socket à connecter (extension de TcpClient)</param>
		/// <param name="host"></param>
		/// <param name="portDistant"></param>
		/// <returns>faux si la connexion a échoué</returns>
		public static bool ConnecterTCP(this TcpClient tcpClient, IPEndPoint remoteEndPoint)
		{
			return tcpClient.ConnecterTCP(remoteEndPoint.Address.ToString(), remoteEndPoint.Port);
		}

		/// <summary>
		/// Microsoft TAP guide (p16, 2012)
		/// For scenarios where data may already be available and simply needs to be returned from a task-returning method 
		/// lifted into a Task<TResult>, the Task.FromResult method may be used.
		/// 
		/// Ce n'est pas notre cas ici, on n'a pas le résultat, il 
		/// faut donc créer une tâche avec Run.
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="host"></param>
		/// <param name="portDistant"></param>
		/// <returns></returns>
		public static Task<bool> ConnecterTCPAsync(this TcpClient tcpClient, string host, int portDistant)
		{
			return Task.Run(() => tcpClient.ConnecterTCP(host, portDistant));
		}




		/// <summary>
		/// Ne "connecte" pas vraiment comme avec TCP. 
		/// Le Connect de UDP permet simplement d'éviter d'avoir à spécifier 
		/// la destination à chaque Send ultérieur.
		/// Mis à part une erreur de résolution DNS, il est pratiquement impossible de faire échouer cette méthode.
		/// </summary>
		/// <param name="udpClient"></param>
		/// <param name="hostname"></param>
		/// <param name="portDistant">le port "remote", cette méthode ne permet pas de choisir le port sortant</param>
		/// <returns></returns>
		public static bool ConnecterUDP(this UdpClient udpClient, string hostname, int portDistant)
		{
			try
			{

				udpClient.Connect(hostname, portDistant);

				if (Verbose)
					WriteLine("Socket UDP prêt. Pas vraiment connecté, mais prêt à recevoir ou envoyer.");

				return true;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.HostNotFound)
			{
				AfficherErreur(Message11001HostNotFound);
			}
			catch (SocketException ex)
			{
				AfficherErreur("Erreur de connexion code : " + ex.ErrorCode);

			}
			return false;
		}

		/// <summary>
		/// Ne "connecte" pas vraiment comme avec TCP. 
		/// Le Connect de UDP permet simplement d'éviter d'avoir à spécifier 
		/// la destination à chaque Send ultérieur.
		/// </summary>
		/// <param name="udpClient">le client</param>
		/// <param name="portDistant">le port "remote", cette méthode ne permet pas de choisir le port sortant</param>
		/// <returns></returns>
		public static bool ConnecterUDPPourDiffusion(this UdpClient udpClient, int portDistant)
		{
			try
			{
				udpClient.Connect(new IPEndPoint(IPAddress.Broadcast, portDistant));

				if (Verbose)
					WriteLine("L'adresse de diffusion {0} sera utilisée.", IPAddress.Broadcast);

				return true;
			}
			catch (SocketException)
			{
				AfficherErreur("Impossible de préparer un socket pour diffusion vers le port {0}.", portDistant);
				return false;
			}
		}

		/// <summary>
		/// Prépare un socket en mode écoute (la file de client pourra commencer)
		/// mais on n'attend pas après le premier client (c'est Accept() qui fera ca).
		/// 
		/// Utile pour un client itératif lorsqu'on veut Accepter explicitement un client à la fois.
		/// </summary>
		/// <param name="port">le port TCP, on prend généralement un nombre < 2000</param>
		/// <returns>un socket avec seulement le localEndPoint de défini</returns>
		public static Socket ÉcouterTCP(int port)
		{
			var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			try
			{
				s.Bind(new IPEndPoint(IPAddress.Any, port));
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) // 10048
			{
				AfficherErreur("Port déja occupé.");
				return null; // c'est au code appellant à tester le résultat
			}

			if (Verbose) AfficherInfo("Bind fait sur interface : " + s.LocalEndPoint);

			s.Listen(FileAttenteTCP); // 5 est assez standard pour la file...normalement le serveur accepte les clients assez rapidement alors ils ne restent pas dans la file
																// À noter que c'est la limite de la file des clients non acceptés. Il pourra y en avoir plus que ca au total.

			if (Verbose) AfficherInfo("Écoute TCP démarée.");

			return s;
		}

		/// <summary>
		/// Méthode tout-en-un qui prépare un socket en mode écoute (la file de clients pourra commencer)
		/// et qui attend après et accepte SEULEMENT le premier client.
		/// 
		/// Écoutera sur toutes les interfaces, ce qui est généralement ce qu'on veut. 
		/// Modifiez le code si ce n'est pas le cas ou rendez le paramétrable.
		/// 
		/// Si vous voulez faire un serveur qui sert plusieurs clients, ce n'est clairement pas cette méthode qu'il vous faut.
		/// 
		/// </summary>
		/// <param name="port"></param>
		/// <returns></returns>
		public static Socket ÉcouterTCPEtAccepter(int port)
		{
			Socket s = ÉcouterTCP(port); // crée le socket avec seulement le point de connexion local
			if (s == null) return null;
			return s.Accept(); // retourne un socket avec les 2 points de connexion ou null si le socket est null
		}

		/// <summary>
		/// Méthode en prévision de la migration vers null aware context
		/// </summary>
		/// <param name="port"></param>
		/// <param name="s"></param>
		/// <returns></returns>
		public static bool ÉcouterTCPEtAccepter(int port, out Socket s)
		{
			Socket masterSocket = ÉcouterTCP(port);
			s = null;
			if (masterSocket == null) return false;
			s = masterSocket.Accept();
			return true;
		}






		/// <summary>
		/// Identique à ÉcouterTCP mais retourne un TcpListener à la place
		/// </summary>
		/// <param name="port"></param>
		/// <returns></returns>
		public static TcpListener ÉcouterTCP_TcpListener(int port)
		{
			try
			{
				var s = new TcpListener(new IPEndPoint(IPAddress.Any, port));
				s.Start();
				return s;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) // 10048
			{
				AfficherErreur("Port déja occupé.");
				return null;
			}

		}

		/// <summary>
		/// Initialise un socket existant UDP pour recevoir
		/// sur toutes les interfaces
		/// </summary>
		/// <returns>vrai si le bind réussi</returns>
		public static bool ÉcouterUDP(int port, out UdpClient clientUDP)
		{
			return ÉcouterUDP(port, out clientUDP, IPAddress.Any);
		}

		/// <summary>
		/// Initialise un socket existant UDP pour recevoir
		/// sur l'interface spécifié
		/// </summary>
		/// <returns>vrai si le bind réussi</returns>
		public static bool ÉcouterUDP(int port, out UdpClient clientUDP, IPAddress ipInterfaceÉcoute)
		{
			try
			{
				clientUDP = new UdpClient();
				var localEndpoint = new IPEndPoint(ipInterfaceÉcoute, port);
				clientUDP.Client.Bind(localEndpoint);

				if (Verbose)
					AfficherInfo("Bind effectué sur : " + localEndpoint.ToString());

				return true;
			}
			catch (SocketException ex) when (ex.ErrorCode == 10049)
			{
				AfficherErreur("vous faites n'importe quoi");
				clientUDP = null;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
			{
				AfficherErreur("Erreur de création du socket. Port UDP déjà en utilisation.");
				clientUDP = null;
			}
			return false;
		}

		/// <summary>
		/// Tente de démarrer l'écoute sur le premier port UDP
		/// disponible à partir du port de début fourni.
		/// </summary>
		/// <param name="portDépart">Port de départ, sera ajusté au port actuel</param>
		/// <param name="clientUDP">L'objet représentant la connexion</param>
		/// <returns>Le port actuel d'écoute ou -1 si impossible de démarrer l'écoute</returns>
		public static bool ÉcouterUDPPremierPortDisponible(int portDépart, out UdpClient clientUDP)
		{
			for (int i = 0; i < 10; ++i)
			{
				try
				{
					clientUDP = new UdpClient();
					var localEndpoint = new IPEndPoint(IPAddress.Any, portDépart + i);
					clientUDP.Client.Bind(localEndpoint);

					return true;
				}
				catch (SocketException)
				{
					AfficherErreur("Erreur de création du socket");
				}
			}
			clientUDP = null;
			return false;
		}

		/// <summary>
		/// ATTENTION, NE MARCHE PAS PARFAITEMENT, MÊME SI PLUSIEURS SOCKET ÉCOUTENT SUR LE MEME PORT UDP, UN SEUL RECOIT LE MESSAGE
		/// CA PEUT ÊTRE ACCEPTABLE DANS CERTAINS CAS, MAIS C'EST RAREMENT CE QU'ON VEUT.
		/// </summary>
		/// <param name="port"></param>
		/// <param name="clientUDP"></param>
		/// <returns></returns>
		public static bool ÉcouterUDPRéutilisable(int port, out UdpClient clientUDP)
		{
			try
			{
				clientUDP = new UdpClient();
				// La réutilisation du socket permettra de partir plusieurs clients NP qui écouteront tous sur le port UDP
				clientUDP.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
				clientUDP.EnableBroadcast = true;
				var localEndpoint = new IPEndPoint(IPAddress.Any, port);

				clientUDP.Client.Bind(localEndpoint);

				return true;
			}
			catch (SocketException)
			{
				WriteLine("Erreur de création du socket");
				clientUDP = null;

			}
			return false;
		}

		/// <summary>
		/// Utilise RecevoirMessage donc lié au buffering TCP
		/// Privilégier EnvoyerEtRecevoirLigne ou un truc du genre ..
		/// </summary>
		/// <param name="c"></param>
		/// <param name="message"></param>
		/// <param name="réponse"></param>
		/// <returns></returns>
		public static bool EnvoyerEtObtenirRéponse(this TcpClient c, string message, out string réponse)
		{
			c.EnvoyerMessage(message);
			return c.RecevoirMessage(out réponse);
		}

		public static bool EnvoyerEtRecevoirLigne(this TcpClient c, string messageÀEnvoyer, out string réponse, bool ajouterFinDeLigne = true)
		{
			c.EnvoyerMessage(messageÀEnvoyer, ajouterFinDeLigne);
			return c.RecevoirLigne(out réponse);

		}

		public static bool EnvoyerEtRecevoirLigneAprèsSentinelle(this TcpClient c, string messageÀEnvoyer, string sentinelle, out string réponse)
		{
			c.EnvoyerMessage(messageÀEnvoyer);
			return c.RecevoirLigneAprèsSentinelle(out réponse, sentinelle);
		}

		public static bool EnvoyerEtRecevoirLigneAprèsSentinelleInvite(this TcpClient c, string messageÀEnvoyer, out string réponse)
		{
			return c.EnvoyerEtRecevoirLigneAprèsSentinelle(messageÀEnvoyer, SentinelleInvite, out réponse);
		}

		public static bool EnvoyerEtRecevoirLigneAprèsSentinelleInfo(this TcpClient c, string messageÀEnvoyer, out string réponse)
		{
			c.EnvoyerMessage(messageÀEnvoyer);
			return c.RecevoirLigneAprèsSentinelleInfo(out réponse);
		}

		/// <summary>
		/// Considérez utiliser TcpClient ou Stream
		/// Cette surcharge permet d'envoyer un array de byte ** complètement ** , sans ajout de ligne et sans paramètre d'encodage.
		/// Utile seulement si on veut envoyez des données binaires possiblement non représentables avec les encodages disponibles ASCII, ANSI (Default) ou UTF8.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="données"></param>
		public static bool EnvoyerMessage(this Socket s, byte[] données)
		{
			return EnvoyerMessage(s, données, données.Length);
		}

		/// <summary>
		/// Considérez utiliser TcpClient ou Stream
		/// Cette surcharge permet d'envoyer un array de byte complètement, sans ajout de ligne et sans paramètre d'encodage.
		/// Permet d'envoyer ** partiellement ** un array de byte.
		/// Utile seulement si on veut envoyez des données binaires possiblement non représentables avec les encodages disponibles ASCII, ANSI (Default) ou UTF8.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="données">le buffer au complet</param>
		/// <param name="taille">le nb d'octets du buffer à envoyer </param>
		public static bool EnvoyerMessage(this Socket s, byte[] données, int taille)
		{
			try
			{
				taille = s.Send(données, taille, SocketFlags.None);
				return true;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
			{
				AfficherErreur("Déconnexion du endpoint distant pendant l'envoi. " +
					"Plutôt rare, cela signifie probablement que le thread bloquait sur le send, " +
					"ce qui veut dire soit que votre solution est mal conçue," +
					"ou que vous êtes en train de débugger et que cela a causer un effet de bord.");
				return false;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionAborted)
			{
				AfficherErreur(Message10053);
				return false;
			}
			catch (SocketException ex)
			{
				AfficherErreur("Erreur de socket au moment de l'envoi. Code : " + ex.ErrorCode + "(" + ex.SocketErrorCode + ")");
				throw;
			}
		}

		/// <summary>
		/// Permet d'envoyer une string dans l'encodage par défaut ou selon l'encodage fourni.
		/// Ajoutera une fin de ligne par défaut, ce qui n'est pas nécessairement ce que vous voulez.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="ajouterFinLigne"></param>
		/// <returns></returns>
		public static bool EnvoyerMessage(this Socket s, string message, bool ajouterFinLigne = true)
		{
			if (ajouterFinLigne)
				message += FinDeLigne;

			AfficherEnvoi(message);

			byte[] données = Encodage.GetBytes(message);

			return EnvoyerMessage(s, données);
		}

		/// <summary>
		/// Redirige l'appel à tcpclient.GetStream(). Vous recevrez une InvalidOperation si vous faite ca sur un socket non connecté/déconnecté !
		/// Non fonctionnera pas si vous êtes sur une connexion sécurisée.
		/// Dans ce cas, utilisez plutot votre propre stream Ssl.
		/// </summary>
		/// <param name=""></param>
		/// <param name="message"></param>
		/// <param name="ajouterFinLigne">fin de ligne Windows</param>
		/// <exception cref="InvalidOperationException">Si le inner stream est non connecté/déconnecté</exception>
		public static bool EnvoyerMessage(this TcpClient client, string message, bool ajouterFinLigne = true)
		{
			//throw new InvalidOperationException("Utilisez la méthode sur votre stream directement");
			return client.GetStream().EnvoyerMessage(message, ajouterFinLigne);
		}

		public static bool EnvoyerMessage(this TcpClient client, char c, bool ajouterFinLigne = true)
		{
			string s = c.ToString(); // ou concaténation
															 //throw new InvalidOperationException("Utilisez la méthode sur votre stream directement");
			return client.GetStream().EnvoyerMessage(s, ajouterFinLigne);
		}

		/// <summary>
		/// Envoi d'un message (paquet) TCP.
		/// La plupart des protocoles sont basés sur le principe de requêtes/réponses et celles-ci sont basées sur 
		/// le concept de "lignes" alors le comportement par défaut de ma librairie est d'ajouter une fin de ligne (configurable).
		/// </summary>
		/// <param name="message">La string du message, incluant ou non une fin de ligne. Elle sera décodée en octets selon l'encodage configuré.</param>
		/// <param name="ajouterFinLigne">Ajoute la fin de ligne configurée, ou non</param>
		/// <returns>vrai si l'envoi est un succès; vous n'êtes pas obligé nécessairement de capter la valeur de retour</returns>
		public static bool EnvoyerMessage(this Stream s, string message, bool ajouterFinLigne = true)
		{
			if (ajouterFinLigne)
				message += FinDeLigne;

			byte[] données = Encodage.GetBytes(message);
			try
			{
				s.Write(données, 0, données.Length);
				AfficherEnvoi(message);
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketEx)
			{
				switch (socketEx.SocketErrorCode)
				{
					case SocketError.ConnectionReset:
						AfficherErreur(Message10054);
						break;
					case SocketError.ConnectionAborted:
						AfficherErreur(Message10053);
						break;
					default:
						AfficherErreur("Erreur inhabituelle non 10053/54 lors de l'envoi.");
						throw;
				}
				return false;
			}
			catch (Exception ex)
			{
				AfficherErreur("Erreur lors de l'envoi sur le stream tcp. Détails exception : " + ex.Message);
				throw;
			}
			return true; // message envoyé avec succès
		}

		/// <summary>
		/// UDP
		/// Utile lorsque le destinaire a déjà été spécifié dans le Connect
		/// </summary>
		/// <param name="s">un client UDP déja connecté</param>
		/// <param name="message"></param>
		public static void EnvoyerMessage(this UdpClient s, string message)
		{
			byte[] données = Encodage.GetBytes(message);
			s.Send(données, données.Length);
			AfficherEnvoi(message);
		}

		/// <summary>
		/// UDP Équivalent du SendTo
		/// </summary>
		/// <param name="s">le client UDP non connecté</param>
		/// <param name="message"></param>
		/// <param name="destinataire">le destinataire</param>
		public static void EnvoyerMessage(this UdpClient s, string message, IPEndPoint destinataire)
		{
			byte[] données = Encodage.GetBytes(message);
			// Ajout 2023-02-21
			int taille = s.Send(données, données.Length, destinataire);
			Debug.Assert(taille == données.Length);
			AfficherEnvoi(message);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="s"></param>
		/// <param name="messageÀRecevoir"></param>
		/// <param name="messageÀEnvoyer"></param>
		/// <param name="ajouterFinLigne"></param>
		public static bool EnvoyerMessageAprès(this Stream s, string messageÀRecevoir, string messageÀEnvoyer, bool ajouterFinLigne = true)
		{
			// recevoir jusqu'à
			if (!s.RecevoirMessagesJusqua(out string _, messageÀRecevoir))
			{
				AfficherErreur("Erreur de réception.");
				return false;
			}

			if (!s.EnvoyerMessage(messageÀEnvoyer, ajouterFinLigne))
			{
				AfficherErreur("Erreur d'envoi après réception.");
				return false;
			}

			return true;
		}

		public static void EnvoyerMessageAvecTimeout(this Socket s, string message, int délaiTimeout, out bool finConnexion, out bool timeout, bool ajouterFinLigne = true)
		{
			timeout = false;
			finConnexion = false;
			int vieuxDélaiTimeout = s.SendTimeout;
			s.SendTimeout = délaiTimeout;
			try
			{
				bool envoiRéussi = EnvoyerMessage(s, message, ajouterFinLigne);
				timeout = !envoiRéussi;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
			{
				if (s.Connected)
					timeout = true;
				else
					finConnexion = true;
			}
			catch (InvalidOperationException ex)
			{
				AfficherErreur(MessageInvalidOperationSocketFermé + ex.Message);
				finConnexion = true;
			}

			s.SendTimeout = vieuxDélaiTimeout;

		}

		/// <summary>
		/// Pour TCP (ou SSL), on ajoute une fin de ligne puisque TCP est stream-orienté, rien ne nous
		/// garanti que le message sera reçu en un morceau sur le fil même si
		/// envoyé en un seul appel à Send à partir d'ici.
		/// </summary>
		/// <param name="s">le buffer interne du socket TCP</param>
		/// <param name="message"></param>
		/// <param name="ajouterFinLigne">mettre à false si votre message contient déja un \n</param> 
		//public static void EnvoyerMessage(this Stream s, string message, bool ajouterFinLigne = true)
		//{
		//	EnvoyerMessage((s, message, ajouterFinLigne);
		//}





		/// <summary>
		/// Envoie la requête (et ajoute la fin de ligne) et 
		/// recoit une ligne représentant la réponse.
		/// Pour les protocoles avec des réponses multilignes, considérez utiliser RecevoirJusquaCode()
		/// </summary>
		/// <param name="s"></param>
		/// <param name="requête"></param>
		/// <param name="réponse"></param>
		/// <returns></returns>
		public static bool EnvoyerRequeteEtObtenirRéponse(this Stream s, string requête, out string réponse)
		{
			s.EnvoyerMessage(requête);
			return s.RecevoirLigne(out réponse);
		}

		/// <summary>
		/// On aimerait qu'un client puisse savoir qu'il a été accepté (et non simplement connecté)
		/// TODO : trouver un API low-level pour faire ca.
		/// La façon la plus simple et habituelle dans les protocoles connus est de spécifier un message de bienvenue 
		/// dans le protocole. Lorsque le serveur accepte un client, il lui envoie un message de 'bienvenue' avec un 
		/// code spécifique. e.g Le protocole FTP envoie un code 220. 
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <returns></returns>
		public static bool EstAccepté(this TcpClient tcpClient)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message">sera affiché après la fermeture</param>
		/// <returns></returns>
		public static bool FermerAvecAffichage(this Stream s, string message)
		{
			// Avec le stream il y a une seule étape et non deux comme avec le socket (shutdown, close)
			s.Close();
			WriteLine(message);
			return true;
		}

		/// <summary>
		/// Affichage dans stdout
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns></returns>
		//public static bool FermerAvecAffichage(this TcpClient tcpClient, string message)
		//{
		//  return tcpClient.FermerAvecAffichage(message);
		//}

		/// <summary>
		/// Offre une granularité plus fine des étapes de fermeture d'une connexion, avec journalisation.
		/// </summary>
		public static void Fermer(this TcpClient tcpClient)
		{
			if (Verbose)
				WriteLine("Fermeture du stream TCP ...");

			tcpClient.GetStream().Close(); // équivalent seulement au Shutdown du socket ?

			if (Verbose)
				WriteLine("Fermeture du client TCP ...");

			tcpClient.Close();

			if (Verbose)
				WriteLine("Fermeture terminée.");
		}

		/// <summary>
		/// Offre une granularité plus fine des étapes de fermeture d'une connexion, avec journalisation.
		/// </summary>
		public static void Fermer(this Socket s)
		{
			if (Verbose)
				WriteLine("Désactivation des communication sur le socket ...");

			// On peut faire shutdown seulement sur un socket connecté
			s.Shutdown(SocketShutdown.Both);

			if (Verbose)
				WriteLine("Communications terminées. Fermeture et libération des ressources en cours...");

			s.Close();

			if (Verbose)
				WriteLine("Fermeture terminée.");
		}

		/// <summary>
		/// Sert à recevoir une ligne et ignorer la valeur de retour.
		/// C'est seulement un bonbon syntaxique pour éviter d'utiliser Recevoir si on a pas besoin de la valeur retournée.
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <returns></returns>
		public static bool IgnorerLigne(this TcpClient tcpClient)
		{
			return tcpClient.GetStream().RecevoirLigne(out _);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="nbLignes"></param>
		/// <returns>vrai si réussi à ignorer nb lignes avec succès</returns>
		public static bool IgnorerLignes(this TcpClient tcpClient, int nbLignes)
		{
			if (nbLignes < 1) throw new ArgumentException("Veuillez spécifier le nombre de lignes");

			for (int i = 0; i < nbLignes; i++)
			{
				if (!tcpClient.IgnorerLigne()) return false;
			}

			return true;

		}

		/// <summary>
		/// Bonbon syntaxique, lire la doc des autres méthodes pour comprendre la librairie
		/// </summary>
		public static bool IgnorerLignesJusquaLigneSeTerminantPar(this Stream s, string suffixeLigne)
		{
			RecevoirLignesJusquaLigneSeTerminantPar(s, out string _, suffixeLigne);
			return true;
		}


		/// <summary>
		/// Sert à tout recevoir et tout ignorer jusqu'à une certaine sentinelle configurable (propriété publique)
		/// C'est seulement un bonbon syntaxique pour éviter d'utiliser RecevoirJusquaInvite si on a pas besoin de la valeur retournée.
		/// </summary>
		public static bool IgnorerJusquaInvite(this TcpClient tcp)
		{
			return tcp.GetStream().RecevoirJusqua(out _, SentinelleInvite);
		}

		/// <summary>
		/// Sert à tout recevoir et tout ignorer jusqu'à une certaine sentinelle fournie en paramètre
		/// C'est seulement un bonbon syntaxique pour éviter d'utiliser RecevoirJusquaSentinelle si on a pas besoin de la valeur retournée.
		/// </summary>
		/// <returns>si on a réussi à recevoir et ignorer la sentinelle</returns>
		public static bool IgnorerJusqua(this TcpClient tcp, string sentinelle)
		{
			return tcp.GetStream().RecevoirJusqua(out _, sentinelle);
		}




		/// <summary>
		/// Permet de créer une connexion TCP tout en fournissant tous les paramètres configurables de la librairie
		/// C'est plus un bonbon syntaxique qu'autre chose. 
		/// </summary>
		/// <param name="host"></param>
		/// <param name="portTCP"></param>
		/// <param name="finLigne"></param>
		/// <param name="verbose"></param>
		/// <param name="sentinelleInvite"></param>
		/// <param name="sentinelleInfo"></param>
		/// <returns></returns>
		internal static TcpClient InitConnexion(string host, int portTCP, string finLigne = "\r\n", bool verbose = false,
																						string sentinelleInvite = ":", string sentinelleInfo = ":")
		{
			FinDeLigne = finLigne;
			Verbose = verbose;
			SentinelleInvite = sentinelleInvite;
			SentinelleInfo = sentinelleInfo;
			return PréparerSocketTCPConnecté(host, portTCP);
		}



		/// <summary>
		/// Encapsulation ultra-digérée des étapes nécessaires pour faire un serveur concurrent.
		/// 1 création du socket qui deviendra le socket maitre
		/// 2 bind sur toutes les interfaces (c'est normalement ce qu'on veut dans 99% des cas)
		/// 3 listen (activation de la file d'attente)
		/// 4 ajout du socket maitre dans une liste de tous les sockets (évidemment, il y en aura qu'un pour le moment)
		/// </summary>
		/// <param name="portÉcoute"></param>
		/// <param name="socketMaitre">le socket servant de guichet pour recevoir les nouveaux clients (analogie avec attribution d'un numéro au CLSC ou à la SAAQ)</param>
		/// <param name="listeComplèteSockets">la liste contenant le socket maitre</param>
		/// <returns>vrai si tout c'est bien déroulé</returns>
		public static bool PréparerServeurConcurrent(int portÉcoute, out Socket socketMaitre, out List<Socket> listeComplèteSockets)
		{
			try
			{
				socketMaitre = ÉcouterTCP(portÉcoute);
			}
			catch (Exception ex)
			{
				AfficherErreur("Impossible de configurer le socket maitre.");
				AfficherErreur(ex.Message);
				socketMaitre = null; // l'initialisation est pour satisfaire le compilateur qui ne veut pas retourner sinon
				listeComplèteSockets = null;
				return false;
			}


			listeComplèteSockets = [socketMaitre]; // création du "guichet"
																												

			return true;

		}



		/// <summary>
		/// Cette méthode va créer un socket et tenter de le connecter au serveur spécifié.
		/// Si vous voulez forcer IPv4, instanciez vous même le TCPClient et appelez Connecter...
		/// </summary>
		/// <param name="host"></param>
		/// <param name="portDistant"></param>
		/// <returns>null si la connexion a échoué, sinon le client lui-même</returns>
		public static TcpClient PréparerSocketTCPConnecté(string host, int portDistant)
		{
			// Si vous voulez forcer IPV4 ou IPv6, il faut spécifier le 1er paramètre de la surcharge à cet effet
			var tcpClient = new TcpClient();

			if (tcpClient.ConnecterTCP(host, portDistant))
				return tcpClient;
			else
				return null;
		}





		/// <summary>
		/// UDP n'est jamais connecté. "Connecter" un socket UDP permet seulement de ne pas avoir à spécifier le destinataire
		/// à chaque envoi, ce qui n'est pas le cas ici.
		/// </summary>
		/// <returns>un socket prêt à être utiliser pour envoyer à n'importe qui, en spécifiant le remoteEndPoint chaque fois </returns>
		public static UdpClient PréparerSocketUDPNonConnectéPourEnvoiMultiple()
		{
			var udpClient = new UdpClient();
			return udpClient;
		}

		public static UdpClient PréparerSocketUDPNonConnectéSurInterface(string ipInterface)
		{
			return PréparerSocketUDPNonConnectéSurInterface(IPAddress.Parse(ipInterface));
		}

		/// <summary>
		/// Sert dans des cas spécifiques où l'on veut un socket non connecté 
		/// et l'associer à une interface en particulier.
		/// </summary>
		/// <param name="ipInterface">le ip local de l'interface</param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPNonConnectéSurInterface(IPAddress ipInterface)
		{
			// Le port 0 signifie "prendre n'importe quel port local disponible"
			// on n'a pas besoin de le connaitre puisque ce socket agira comme client
			var udpClient = new UdpClient(new IPEndPoint(ipInterface, 0));
			return udpClient;
		}

		/// <summary>
		/// En étant connecté, on recoit et envoie toujours au même
		/// point de connexion
		/// </summary>
		/// <param name="hostname"></param>
		/// <param name="portDistant"></param>
		/// <param name="localEndPoint">on utilise ce paramètre seulement pour des cas spécifiques, comme quand on veut absolument que le traffic sorte par un interface spécifique, laissez le null dans le doute</param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPConnecté(string hostname, int portDistant, IPAddress ipInterfaceLocal = null)
		{
			UdpClient udpClient;
			if (ipInterfaceLocal == null)
				udpClient = new UdpClient();
			else
				udpClient = new UdpClient(new IPEndPoint(ipInterfaceLocal, 0)); // n'importe quel port

			udpClient.ConnecterUDP(hostname, portDistant);
			return udpClient;
		}

		public static UdpClient PréparerSocketUDPConnectéPourDiffusionSurInterface(int portDistant, string ipInterface)
		{
			UdpClient u = PréparerSocketUDPNonConnectéSurInterface(ipInterface);
			u.ConnecterUDPPourDiffusion(portDistant);
			return u;
		}

		/// <summary>
		/// Utilise l'interface par défaut
		/// </summary>
		/// <param name="portDistant"></param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPConnectéPourDiffusion(int portDistant)
		{
			var udpClient = new UdpClient();

			udpClient.ConnecterUDPPourDiffusion(portDistant);
			return udpClient;
		}





		/// <summary>
		/// Créé un socket UDP et l'initialise pour recevoir
		/// sur l'interface spécifié
		/// </summary>
		/// <param name="port"></param>
		/// <param name="ipInterfaceÉcoute">ou fournir Any pour écouter sur tous les interfaces</param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPPourRéception(int port, IPAddress ipInterfaceÉcoute)
		{
			UdpClient udpClient;
			if (!ÉcouterUDP(port, out udpClient, ipInterfaceÉcoute))
				AfficherErreur("Impossible de préparer l'écoute.");

			return udpClient; // peu importe si c'est null

		}


		/// <summary>
		/// Créé un socket UDP et l'initialise pour recevoir
		/// sur toutes les interfaces.
		/// Lorsqu'on agit comme serveur, on veut généralement choisir son port. 
		/// Si vous voulez simplement créer un socket udp sur n'importe quel port, utilisez PréparerSockerUDP()
		/// </summary>
		/// <param name="port"></param>
		/// <returns></returns>
		public static UdpClient PréparerSocketUDPPourRéception(int port)
			=> PréparerSocketUDPPourRéception(port, IPAddress.Any);


		private static void RecevoirAvecTimeout(this Socket socket, MéthodeRéceptionAvecTimeoutSocket méthodeRéception, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			timeout = false;
			finConnexion = false;
			message = string.Empty;

			int vieuxDélaiTimeout = socket.ReceiveTimeout;
			socket.ReceiveTimeout = délaiTimeout;

			try
			{
				bool succès = méthodeRéception(socket, out message, out timeout);
				finConnexion = !succès && !timeout;
				Debug.Assert(Convert.ToInt32(finConnexion) + Convert.ToInt32(timeout) <= 1);
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketException && socketException.SocketErrorCode == SocketError.TimedOut)
			{
				// le problème est que ca donne le même code pour un vrai timeout et une déconnexion
				if (socket.Connected)
					timeout = true;
				else
					finConnexion = true;
			}
			catch (InvalidOperationException ex)
			{
				AfficherErreur(MessageInvalidOperationSocketFermé + ex.Message);
				finConnexion = true;
			}
			// On remet le timeout correctement, probablement à 0 (blocking)
			socket.ReceiveTimeout = vieuxDélaiTimeout;

		}

		delegate bool MéthodeRéceptionAvecTimeoutStream(Stream stream, out string message, out bool timeout);

		delegate bool MéthodeRéceptionAvecTimeoutTcpClient(TcpClient tcpClient, out string message, out bool timeout);

		private static void RecevoirAvecTimeout(this Stream stream, MéthodeRéceptionAvecTimeoutStream méthodeRéception, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			timeout = false;
			finConnexion = false;
			message = string.Empty;

			int vieuxDélaiTimeout = stream.ReadTimeout; // normalement 0 (blocking)
			stream.ReadTimeout = délaiTimeout;
			try
			{
				bool reçuQuelqueChose = méthodeRéception(stream, out message, out timeout);
				finConnexion = !reçuQuelqueChose && !timeout;
				Debug.Assert(Convert.ToInt32(finConnexion) + Convert.ToInt32(timeout) <= 1);

			}
			catch (IOException ex) when (ex.InnerException is SocketException socketException && socketException.SocketErrorCode == SocketError.TimedOut)
			{
				timeout = true;
			}
			catch (InvalidOperationException ex)
			{
				AfficherErreur(MessageInvalidOperationSocketFermé + ex.Message);
				throw;
			}
			// On remet le timeout correctement, probablement à 0 (blocking)
			stream.ReadTimeout = vieuxDélaiTimeout;
		}

		private static void RecevoirAvecTimeout(this TcpClient tcpClient, MéthodeRéceptionAvecTimeoutStream méthodeRéception, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			RecevoirAvecTimeout(tcpClient.GetStream(), méthodeRéception, out message, délaiTimeout, out finConnexion, out timeout);
		}

		/// <summary>
		/// Voir <seealso cref="RecevoirJusqua(Stream, out string, string)"/>
		/// </summary>
		public static bool RecevoirJusqua(this TcpClient tcp, out string message, string messageARecevoir)
		{
			try
			{
				return tcp.GetStream().RecevoirJusqua(out message, messageARecevoir);
			}
			catch (InvalidOperationException)
			{
				AfficherErreur(MessageInvalidOperationSocketFermé);
				throw;
			}
		}

		public static bool RecevoirJusqua(this Socket socket, out string message, string messageARecevoir)
		{
			var ns = new NetworkStream(socket);
			return ns.RecevoirJusqua(out message, messageARecevoir);
		}

		/// <summary>
		/// Recevoir jusqu'à la sentinelle spécifiée. 
		/// Recoit caractère par caractère au lieu de par ligne ou pas message TCP
		/// </summary>
		/// <param name="message"></param>
		/// <param name="messageARecevoir">la sentinelle</param>
		public static bool RecevoirJusqua(this Stream s, out string message, string messageARecevoir)
		{
			try
			{
				message = "";
				bool succès = false;
				int c;
				for (; ; )
				{
					c = s.ReadByte();
					/***/
					if (c == -1) break;
					/***/

					message += (char)c;

					if (message.EndsWith(messageARecevoir))
					{
						succès = true;
						break;
					}
				}

				AfficherRéception(message);

				if (!succès) message = null;

				return succès; // corrigé 2016-05-19 au lieu de return true
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionReset)
			{
				AfficherErreur(Message10054);
			}
			catch (Exception ex)
			{
				AfficherErreur(ex.Message);
				throw;
			}
			message = null;
			return false;
		}

		public static bool RecevoirLigneAprèsSentinelle(this TcpClient c, out string réponse, string sentinelle)
		{
			c.IgnorerJusqua(sentinelle);
			return c.RecevoirLigne(out réponse);
		}



		public static Task<(bool succès, string message)> RecevoirLignesJusquaLigneVideAsync(this TcpClient s)
		{
			return Task.Run(() =>
			{
				bool res = s.GetStream().RecevoirLignesJusquaLigneCodée(out string message, "");
				return (res, message);
			});
		}

		public static bool RecevoirLignesJusquaLigneVide(this TcpClient s, out string message)
		{
			return s.GetStream().RecevoirLignesJusquaLigneCodée(out message, "");
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="ligneCodée"></param>
		/// <returns></returns>
		public static bool RecevoirLignesJusquaLigneCodée(this TcpClient s, out string message, string ligneCodée)
		{
			return s.GetStream().RecevoirLignesJusquaLigneCodée(out message, ligneCodée);
		}

		/// <summary>
		/// Permet de recevoir les lignes incluant les fins de ligne.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message">sera disponible seulement si une ligne codée a été reçue avec succès</param>
		/// <param name="code">On cherche une ligne entière spécifique ayant cette valeur. Pourrait être "." pour POP3 ou simplement "" pour une ligne vide</param>
		/// <returns>vrai si on a réussi à détecter la ligne codée</returns>
		/// <remarks>En fait, les fins de ligne seront retirées lors de la réception et
		/// remises par la suite. Ce n'est pas optimal, privilégiez RecevoirToutJusquaLigneCodée et faire le traitement vous-même. </remarks>
		public static bool RecevoirLignesJusquaLigneCodée(this Stream s, out string message, string ligneCodée)
		{
			message = ""; // on va accumuler des lignes
										// Modification : on ne retourne pas la ligne codée elle-même
			bool premièreLigne = true;
			bool succès = false;
			while (s.RecevoirLigne(out string ligne))
			{
				if (ligne == ligneCodée)
				{
					succès = true;
					break;
				}

				if (!premièreLigne)
					message += FinDeLigne;
				else
					premièreLigne = false;

				message += ligne;
			}
			// Si on n'a pas réussi à recevoir la ligne codée (EOS ou déconnexion 10054 sont les 2 autres scénarios possibles, car sinon ca bloquera)
			//  on null le message
			if (!succès) message = null;

			return succès;
		}


		/// <summary>
		/// Redirige l'appel à tcpclient.GetStream()
		/// Non fonctionnera pas si vous êtes sur une connexion sécurisée.
		/// Dans ce cas, utilisez plutot votre propre stream Ssl.
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns>vrai si quelque chose a été reçu</returns>
		/// <exception cref="InvalidOperationException">Si le inner stream est non connecté/déconnecté</exception>
		public static bool RecevoirLigne(this TcpClient tcpClient, out string message)
		{
			try
			{
				return tcpClient.GetStream().RecevoirLigne(out message);
			}
			catch (InvalidOperationException)
			{
				AfficherErreur(MessageInvalidOperationSocketFermé);
				throw;
			}
		}

		public static bool RecevoirLigne(this TcpClient tcpClient, out string message, out bool timeout)
		{
			return tcpClient.GetStream().RecevoirLigne(out message, out timeout);
		}


		public static void RecevoirLigneAvecTimeout(this Socket socket, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			//MéthodeRéceptionAvecTimeoutSocket m = RecevoirLigne;
			RecevoirAvecTimeout(socket, RecevoirLigne, out message, délaiTimeout, out finConnexion, out timeout);
		}

		public static void RecevoirLigneAvecTimeout(this Stream stream, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			RecevoirAvecTimeout(stream, RecevoirLigne, out message, délaiTimeout, out finConnexion, out timeout);
		}

		public static void RecevoirLigneAvecTimeout(this TcpClient tcpClient, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			RecevoirAvecTimeout(tcpClient, RecevoirLigne, out message, délaiTimeout, out finConnexion, out timeout);
		}


		/// <summary>
		/// Bonbon syntaxique pour certaines situations et alléger le code.
		/// Va recevoir une ligne entière après avoir ignoré tout jusqu'à la sentinelle d'invite préconfigurée.
		/// </summary>
		public static bool RecevoirLigneAprèsInvite(this TcpClient tcpClient, out string message)
		{
			tcpClient.IgnorerJusquaInvite();
			return tcpClient.RecevoirLigne(out message);
		}

		public static bool RecevoirLigneAprèsSentinelleInfo(this TcpClient tcpClient, out string réponse)
		{
			tcpClient.IgnorerJusqua(SentinelleInfo);
			return tcpClient.RecevoirLigne(out réponse);
		}

		/// <summary>
		/// À proscrire. Ne pas utiliser sauf pour illustrer le problème de performance.
		/// </summary>
		/// <param name="s"></param>
		/// <returns></returns>
		public static int LireChar(this Socket s)
		{
			byte[] octets = new byte[1];

			int taille = s.Receive(octets, 1, SocketFlags.None);
			Debug.Assert(taille == 1);

			return octets[0];
		}

		/// <summary>
		/// La meilleure solution est plutot d'utiliser un networkStream si vous en avez un .
		///  e.g networkStream.RecevoirLigne()
		///  
		/// Notre RecevoirLigne applique la même logique que StreamReader.ReadLine. Voici un extrait de la documentation :
		///   A line is defined as a sequence of characters followed by a line feed ("\n"), 
		///   a carriage return ("\r"), or a carriage return immediately followed by a line feed ("\r\n").
		/// </summary>
		/// <returns>true si reçu quelque chose (même ligne vide, car une ligne vide est en fait \n ou \r\n), retourne false si déconnexion normale ou abrupte (10054)</returns>
		public static bool RecevoirLigne(this Socket s, out string message)
		{
			return s.RecevoirLigne(out message, out _);
		}


		private static bool RecevoirLigne(this Socket s, out string message, out bool timeout)
		{
			// Si vous recevez une erreur sur cette ligne, c'est que vous avez closer le socket mais que vous réessayez 
			// de recevoir dessus. Vous avez surement oublié un break; ou un truc comme ca. La solution n'est surement 
			// pas de catcher l'exception mais plutot de corriger votre code.
			var ns = new NetworkStream(s);
			return ns.RecevoirLigne(out message, out timeout);
		}

		/// <summary>
		/// On utilise normalement pas de out parameters (TAP guide, Microsoft, 2012)
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns></returns>
		public static Task<(bool succès, string message)> RecevoirLigneAsync(this TcpClient tcpClient)
		{

			return Task.Run(() =>
			{
				bool res = tcpClient.RecevoirLigne(out string message);
				return (res, message);
			});
		}

		/// <summary>
		/// Pour TCP seulement (UDP n'utilise pas de Stream de toute façon).
		/// Va attendre de recevoir la séquence \r\n ou \n et bloquer (attendre) au besoin.
		/// Utile pour supporter des communications depuis un client Windows (\r\n) et Linux/WSL (\n)
		/// </summary>
		/// <param name="message">la ligne, sans la fin de ligne</param>
		/// <returns>Vrai si une ligne en bonne et due forme a été recue, faux si déconnecté, si rien recu ou si la ligne n'est pas bien formée</returns>
		/// <remarks>Vous pourriez recevoir vrai et être quand même déconnecté. Il y a la propriété Connected du socket qui sera mise à jour mais on ne l'utilise pas en général. On procède à tout recevoir et ensuite on détecte la fin de connexion.</remarks>
		public static bool RecevoirLigne(this Stream s, out string message)
		{
			return s.RecevoirLigne(out message, out _);
		}

		/// <summary>
		/// Cette méthode est privée, car en principe si vous n'utilisez pas de timeout, vous utilisez la surcharge sans ce paramètre.
		/// Si vous voulez utiliser un timeout, utilisez RecevoirLigneAvecTimeout.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="timeout"></param>
		/// <returns></returns>
		private static bool RecevoirLigne(this Stream s, out string message, out bool timeout)
		{
			timeout = false;
			try
			{
				var données = new List<byte>();
				int c;
				for (; ; )
				{
					c = s.ReadByte();

					// Si on rencontre EOS c'est qu'on n'a pas reçu de ligne correctement formée, on retourne rien avec un échec
					if (c == -1)// sentinelle prévue de la librairie System.Net pour EndOfStream
					{
						// Pas besoin de rien afficher ici, car c'est une déconnexion normale.
						message = null;
						return false;
					}

					if (c == '\n') break; // on ne l'ajoute pas

					données.Add((byte)c);
				}

				// On retire le dernier \r si présent
				if (données.Count > 0 && données.Last() == '\r')
					données.RemoveAt(données.Count - 1);

				if (données.Count == 0)
				{
					message = ""; // 2022-03-30
												// 2022-03-30 une ligne vide sera considérée comme si "quelque chose a été reçu", car avec nc c'est possible d'envoyer un message vide.
												// avec nos programmes cela n'arrive pas car les ReadLine() retournent au minimum 1 char ...
					return true;
				}

				// données.ToArray() à remplacer par [.. données] avec C# 12+
				message = Encodage.GetString(données.ToArray(), 0, données.Count);

				AfficherRéception(message);
				Debug.Assert(message.Length > 0);
				return true; // corrigé 2016-05-19 au lieu de return true 
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
			{
				AfficherErreur(Message10054);
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketEx)
			{
				if (socketEx.SocketErrorCode == SocketError.ConnectionReset)
					AfficherErreur(Message10054);
				else if (socketEx.SocketErrorCode == SocketError.TimedOut)
					timeout = true; // pas besoin d'afficher d'erreur pour ca
				else
					AfficherErreur("Déconnexion abrupte avec code non diagnostiqué.");
			}
			message = null;
			return false;
		}



		public static bool RecevoirLignesJusquaLigneSeTerminantPar(this Stream s, out string message, string suffixeLigne)
		{
			string ligne;
			while (s.RecevoirLigne(out ligne) && !ligne.EndsWith(suffixeLigne)) ;
			message = ligne;
			return true;
		}

		/// <summary>
		/// Retoure un message dans l'encodage spécifié en paramètre de la classe NetworkUtils.
		/// Cette fonction bloquera jusqu'à ce qu'un message ne soit reçu, ou jusqu'à une déconnexion du client.
		/// </summary>
		/// <param name="s">le socket sur lequel on fera la réception</param>
		/// <param name="message"></param>
		/// <returns>vrai si reçu quelque chose, faux lors de déconnexion normale ou anormale, timeout ou pour erreur générale; si vous avez besoin de plus de détails sur la raison, veuillez utiliser une autre surcharge</returns>
		public static bool RecevoirMessage(this Socket s, out string message)
		{
			return s.RecevoirMessage(out message, out _); // C# 7 discard
		}

		/// <summary>
		/// Méthode privée. Prenez celle sans le paramètre timeout si le timeout ne vous intéresse pas. 
		/// Sinon, prenez RecevoirAvecTimeout.
		/// </summary>
		private static bool RecevoirMessage(this Socket s, out string message, out bool timeout)
		{
			timeout = false;
			try
			{
				byte[] données = new byte[256];
				int taille = s.Receive(données);
				message = Encodage.GetString(données, 0, taille);
				//finConnexion = taille == 0; // si on a reçu quelque chose, il n'y a pas de déconnexion tout court
				return taille > 0; // reçu quelque chose 
			}

			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
			{
				AfficherErreur(Message10054);
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
			{
				timeout = true;
			}
			catch (SocketException ex)
			{
				AfficherErreur("Erreur anormale de réception code : " + ex.ErrorCode);
			}
			message = null;
			return false;
		}

		delegate bool MéthodeRéceptionAvecTimeoutSocket(Socket s, out string message, out bool timeout);


		public static void RecevoirMessageAvecTimeout(this Socket socket, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			RecevoirAvecTimeout(socket, RecevoirMessage, out message, délaiTimeout, out finConnexion, out timeout);
		}


		/// <summary>
		/// Utilisez seulement cette méthode pour recevoir des données non textuelles
		/// ou pour une chaine que vous ne connaissez pas nécessairement l'encodage.
		/// 
		/// Si vous travaillez avec un protocole basé sur des lignes (requêtes/réponses), 
		///  il serait préférable d'utiliser RecevoirLigne()
		/// </summary>
		/// <param name="données">un buffer déja alloué</param>
		/// <param name="taille">le nb d'octets écrits dans le buffer</param>
		/// <returns></returns>
		public static bool RecevoirMessage(this Socket s, ref byte[] données, out int taille)
		{
			try
			{
				taille = s.Receive(données);
				return taille > 0; // corrigé 2018-03-06 au lieu de return true toujours
			}
			catch (SocketException ex)
			{
				// Il est normal d'avoir une erreur 10054 connection reset quand l'autre détruit la connexion sans appeler un close en bonne et dû forme
				if (ex.ErrorCode != 10054) // For more information about socket error codes, see the Windows Sockets version 2 API error code documentation in MSDN.
					AfficherErreur("Erreur anormale de réception code : " + ex.ErrorCode);
				données = null;
				taille = 0;
				return false;
			}
		}

		/// <summary>
		/// Recoit des messages TCP jusqu'à ce que le message complet se termine par la valeur spécifiée.
		/// Privilégier RecevoirJusqua si vous n'êtes pas certain de comment les messages TCP sont envoyés.
		/// Privilégier RecevoirLignesJusqua si vous savez que ce que vous cherchez sera une fin de ligne.
		/// </summary>
		public static bool RecevoirMessagesJusqua(this TcpClient tcpClient, out string message, string messageÀRecevoir)
		{
			return tcpClient.GetStream().RecevoirMessagesJusqua(out message, messageÀRecevoir);
		}
		/// <summary>
		/// Recoit des messages TCP jusqu'à ce que le message complet se termine par la valeur spécifiée.
		/// Privilégier RecevoirJusqua si vous n'êtes pas certain de comment les messages TCP sont envoyés.
		/// Privilégier RecevoirLignesJusqua si vous savez que ce que vous cherchez sera une fin de ligne.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="messageÀRecevoir">on va vérifier avec EndsWith</param>
		/// <returns></returns>
		public static bool RecevoirMessagesJusqua(this Stream s, out string message, string messageÀRecevoir)
		{
			bool succès = false;
			message = "";
			string portionDuMessage;
			while (true)
			{
				if (!s.RecevoirMessage(out portionDuMessage)) break;

				message += portionDuMessage;
				/***/
				if (portionDuMessage.EndsWith(messageÀRecevoir))
				{
					succès = true;
					break;
				}
				/***/

			}
			if (!succès) message = null;
			return succès;
		}



		/// <summary>
		/// Recoit tout (ligne ou non) jusqu'au point (utile pour le protocole POP3 et autre...)
		/// Le point est exclu.
		/// 
		/// Bug connu : pourrait ne pas détecter le .\r\n si d'autre contenu a été 
		/// envoyé d'avance par le serveur. Par exemple si on envoi LIST et une autre commande
		/// avant de faire le premier Recevoir(). 
		/// 
		/// De toute facon, vous devriez normalement récupérer les réponses
		/// du serveur au fur et à mesure et ce problème n'arrivera pas.
		/// 
		/// Pour corriger complètement, il faudrait y aller caractère par
		/// caractère comme la fonction RecevoirLigne()
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <returns>vrai si succès</returns>
		public static bool RecevoirMessagesJusquaPoint(this Stream s, out string message)
		{
			message = "";
			string portionDuMessage;
			while (true)
			{
				if (!s.RecevoirMessage(out portionDuMessage)) return false;

				message += portionDuMessage;
				if (portionDuMessage.EndsWith(".\r\n"))
				{
					// TODO : enlever le .\r\n
					break;
				}

			}
			return true;
		}



		/// <summary>
		/// Méthode bonbon syntaxique qui permet d'obtenir le Stream. 
		/// Elle vous donnera un message significatif dans l'exception si jamais vous tentez de faire ca sur une connexion déjà fermée,
		/// ce qui arrive souvent quand on oublie de faire break dans un boucle :)
		/// </summary>
		public static Stream ObtenirStream(this TcpClient tcpClient)
		{
			try
			{
				return tcpClient.GetStream();
			}
			catch (InvalidOperationException ex)
			{
				AfficherErreur(MessageInvalidOperationSocketFermé + ex.Message);
				throw;
			}
		}

		/// <summary>
		/// Redirige l'appel à tcpclient.GetStream()
		/// Non fonctionnera pas si vous êtes sur une connexion sécurisée.
		/// Dans ce cas, utilisez plutot votre propre stream Ssl.
		/// </summary>
		/// <param name="tcpClient"></param>
		/// <param name="message"></param>
		/// <returns>vrai si quelque chose a été reçu (succès)</returns>
		public static bool RecevoirMessage(this TcpClient tcpClient, out string message)
		{
			return tcpClient.ObtenirStream().RecevoirMessage(out message);

		}

		/// <summary>
		/// Cette méthode est privée, utilisez la surcharge sans le timeout, et si vous voulez un timeout, utilisez RecevoirAvecTimeout
		/// </summary>
		public static bool RecevoirMessage(this TcpClient tcpClient, out string message, out bool timeout)
		{
			return tcpClient.GetStream().RecevoirMessage(out message, out timeout);
		}

		/// <summary>
		/// UDP Cette fonction est un "wrapper" de la méthode Receive().
		/// Va attendre jusqu'à ce qu'un paquet soit reçu.
		/// </summary>
		/// <param name="s">le socket sur lequel il faut recevoir</param>
		/// <param name="remoteEndPoint">stockera le point de connexion de l'émetteur (IP et port)</param>
		/// <param name="message">le contenu du paquet recu encodé en string</param>
		/// <returns>retourne faux si la réception à échouée</returns>
		public static bool RecevoirMessage(this UdpClient s, ref IPEndPoint remoteEndPoint, out string message)
		{
			try
			{
				byte[] données = s.Receive(ref remoteEndPoint);
				message = Encodage.GetString(données);
				AfficherRéception(message);
				return true;
			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
			{
				if (Verbose)
					AfficherInfo("Timeout sur le socket.");

				throw; // si vous ne voulez pas catcher l'exception, utilisez RecevoirMessageAvecTimeout et je vous retournerai un beau booléen à la place
			}
			catch (SocketException ex)
			{
				// Probablement un code 10054. Possible surtout si c'est en boucle locale (i.e si l'autre point de connexion est sur la même machine)
				// ou un 10060 (TimeOut) si l'UdpClient a un receiveTimeout de spécifié (ce qui n'est pas le cas par défaut)
				AfficherErreur("Erreur de socket au moment de la réception UDP.\n" +
																"Code : " + ex.ErrorCode + "(" + ex.SocketErrorCode + ")");
			}
			catch (ObjectDisposedException ex)
			{
				AfficherErreur("Erreur de socket au moment de la réception,\n" +
																"le socket a été fermé.\n" +
																ex.Message);
			}
			message = null;
			return false;
		}

		/// <summary>
		/// J'ai généralisé cette fonction de NetworkStream à Stream pour supporter SslStream
		/// Si vous implémentez un protocole texte basé sur des lignes entières,
		/// considérez utiliser RecevoirLigne() ou RecevoirJusquaCode()
		/// Le message sera de taille maximale de 256 octets ce qui n'est
		/// pas un problème en soi puisqu'on peut appeller cette méthode en boucle.
		/// Considérez utiliser RecevoirTout() si vous voulez recevoir tout jusqu'à la fin de la connexion.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="message"></param>
		/// <param name="encoding"></param>
		/// <returns>vrai si quelque chose a été reçu (succès), faux indique une fin de connexion</returns>
		public static bool RecevoirMessage(this Stream s, out string message)
		{
			return s.RecevoirMessage(out message, out _);
		}

		public static bool RecevoirMessage(this Stream s, out string message, out bool timeout)
		{
			timeout = false;
			try
			{
				byte[] données = new byte[256];
				int taille = s.Read(données, 0, données.Length); // 0 indique EOS (end of stream)
				message = Encodage.GetString(données, 0, taille);

				AfficherRéception(message); // Pas besoin de tester la verbosité ici
				if (message.Length == 0) message = null;
				return taille > 0; // au lieu de return true toujours, une réception de taille 0 signifie toujours la fin de connexion (normale)
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.TimedOut)
			{
				timeout = true;
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionReset)
			{
				AfficherErreur(Message10054);
			}
			catch (IOException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.ConnectionAborted)
			{
				AfficherErreur(Message10053);
			}
			catch (IOException ex)
			{
				AfficherErreur("Erreur non diagnostiquée. " + ex.Message);
				throw;
			}
			catch (ObjectDisposedException ex)
			{
				AfficherErreur("Erreur de réception, le stream est fermé ou erreur de lecture sur le réseau. " + ex.Message);
			}

			message = null;
			return false;
		}

		public static void RecevoirMessageAvecTimeout(this TcpClient tcpClient, out string message, int délaiTimeout, out bool finConnexion, out bool timeout)
		{
			RecevoirAvecTimeout(tcpClient, RecevoirMessage, out message, délaiTimeout, out finConnexion, out timeout);
		}

		public static bool RecevoirMessageAvecTimeout(this UdpClient s, int délaiTimeout, ref IPEndPoint remoteEndPoint, out string message, out bool timeout)
		{
			timeout = false;
			message = string.Empty;

			int vieuxDélaiTimeout = s.Client.ReceiveTimeout; // normalement 0 (blocking)
			s.Client.ReceiveTimeout = délaiTimeout;
			bool recuQuelqueChose;
			try
			{
				recuQuelqueChose = s.RecevoirMessage(ref remoteEndPoint, out message);

			}
			catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
			{
				// on affiche pas le message d'erreur, c'était prévisible !
				timeout = true;
				recuQuelqueChose = false; // déja par défaut
			}
			catch (Exception ex)
			{
				AfficherErreur(ex.Message);
				throw;
			}

			s.Client.ReceiveTimeout = vieuxDélaiTimeout;

			return recuQuelqueChose;
		}

		/// <summary>
		/// Cette méthode permet de fournir un délai et d'attendre
		/// 
		/// On aurait pu aussi utiliser le modèle async-await
		/// ou faire du polling sur UdpClient.Available
		/// </summary>
		/// <param name="s"></param>
		/// <param name="délaiTimeout"></param>
		/// <param name="endPoint"></param>
		/// <param name="message"></param>
		/// <returns>vrai si un message a été recu</returns>
		public static bool RecevoirMessageAvecTimeoutTask(this UdpClient s, int délaiTimeout, ref IPEndPoint endPoint, out string message, out bool timeout)
		{
			timeout = false;
			try
			{

				// option 1 : avec la méthode async et wait sur la tâche
				// La tâche ne s'annule pas donc malgré le timeout, un problème persistait car la tâche
				// récupérait quand même la donnée du buffer
				Task<UdpReceiveResult> t = s.ReceiveAsync();
				t.Wait(délaiTimeout);
				if (!t.IsCompleted)
					throw new TimeoutException("Trop long");

				byte[] données = t.Result.Buffer;
				endPoint = t.Result.RemoteEndPoint;
				message = Encodage.GetString(données);

				return true;
			}
			catch (AggregateException aex)
			{
				// Si on ferme le serveur local PENDANT que la tâche est démarrée
				if (aex.InnerException is SocketException sex && sex.SocketErrorCode == SocketError.ConnectionReset)
				{
					AfficherErreur("Erreur de réception UDP");
				}
			}
			catch (SocketException ex)
			{
				// Probablement un code 10054 
				AfficherErreur("Erreur de socket au moment de la réception UDP.\n" +
																"Code : " + ex.ErrorCode + "(" + ex.SocketErrorCode + ")");
			}
			catch (ObjectDisposedException ex)
			{
				AfficherErreur("Erreur de socket au moment de la réception,\n" +
																"le socket a été fermé.\n" +
																ex.Message);
			}
			catch (TimeoutException)
			{
				timeout = true;
				AfficherErreur("Le délai de réception est expiré.");
			}
			message = null;
			return false;
		}

		/// <summary>
		/// Voir <seealso cref="RecevoirTout(TcpClient, out string)"/>
		/// </summary>
		public static bool RecevoirTout(this Socket socket, out string messageComplet)
		{
			var ns = new NetworkStream(socket);
			return ns.RecevoirTout(out messageComplet);
		}

		/// <summary>
		/// Surcharge pour TcpClient<br></br>
		/// Voir <see cref="RecevoirTout(Stream, out string)"/> pour documentation complète.
		/// </summary>
		/// <see cref="RecevoirTout(Stream, out string)"/>
		/// <seealso cref="RecevoirTout(Socket, out string)"/>
		public static bool RecevoirTout(this TcpClient tcpClient, out string messageComplet)
		{
			return tcpClient.ObtenirStream().RecevoirTout(out messageComplet);
		}

		/// <summary>
		/// Si vous appellez cette méthode, une réception sera faite
		/// jusqu'à temps que la connexion soit fermée par l'autre point de connexion et ce,
		/// peu importe si c'est une déconnexion normale ou anormale (10054).
		/// </summary>
		/// <param name="s"></param>
		/// <param name="messageComplet"></param>
		/// <remarks>Ne bloque pas si appellé sur une connexion fermé</remarks>
		/// <returns>vrai indique que quelque chose à été reçu (succès)</returns>
		/// 
		public static bool RecevoirTout(this Stream s, out string messageComplet)
		{
			// Pseudo détection pour voir si la connexion est fermée puisque pas accès au TcpClient d'ici
			// Si CanRead est toujours à true, la réception retournera instantanément et ne bloquera pas.
			// 
			if (!s.CanRead) throw new InvalidOperationException("Ne pas appeler sur une connexion déja fermée.");

			string message;
			messageComplet = "";
			bool recuQuelqueChose = false;
			while (s.RecevoirMessage(out message))
			{
				recuQuelqueChose = true;
				messageComplet += message;
			}
			if (!recuQuelqueChose) messageComplet = null;
			return recuQuelqueChose;
		}

		/// <summary>
		/// L'idée est de recevoir tout sur une connexion TCP
		/// qui n'est pas fermée après par le serveur (keep-alive).
		/// L'idéal serait plutot de recevoir jusqu'à un certain code
		/// comme une fin de ligne, une ligne spéciale vide ou contenant
		/// seulement un point (POP3) ou simplement de recevoir
		/// la quantité prévue. En HTTP le serveur vous donne le content-length
		/// alors vous pouvez vous en servir.
		/// Sinon, si vous pouvez vous servir de cette méthode (moins efficace)
		/// qui regarde dans le flux à savoir s'il y a quelque chose
		/// qui n'a pas été recu (lu).
		/// </summary>
		/// <param name="s"></param>
		/// <param name="messageComplet"></param>
		/// <returns></returns>
		public static bool RecevoirToutKeepAlive(this TcpClient s, out string messageComplet)
		{
			//throw new NotImplementedException("TODO");
			string message;
			messageComplet = "";
			bool recuQuelqueChose = false;
			while (/* s.Connected && */ s.Available > 0)
			{
				if (!s.RecevoirMessage(out message)) break;

				recuQuelqueChose = true;
				messageComplet += message;


			}
			if (!recuQuelqueChose) messageComplet = null;
			return recuQuelqueChose;
		}



	}


}
