# Projet_Comunication_TCP

## Auteurs :
- Fabrice Senécal
- Alex Héroux

### Contributeurs :
- PMC

serveur askgod like qui permet la communication entre un client et un serveur se trouvant sur un même réseau.

L'objectif est de développer un système client-serveur pour gérer une compétition de sécurité informatique (CTF) inspirée de **AskGod NSec**.

## Fonctionnalités principales :

### Client
Le client se connecte au serveur via deux modes : soit par découverte automatique (UDP), soit en spécifiant l'IP et le port. Après connexion et réception d'un **message de bienvenue**, le client s'enregistre dans une équipe et utilise des commandes pour interagir avec le serveur (soumettre des flags, consulter le score, etc.).

### Serveur
Le serveur utilise un modèle multithreadé, où chaque client a son propre thread. Il diffuse régulièrement sa présence et gère les connexions des clients, leur envoi du message de bienvenue, ainsi que les commandes (soumission de flags, enregistrement d'équipe, etc.). Le serveur maintient la liste des joueurs, des équipes, des challenges et des scores.

### Sécurisation
La communication client-serveur se fait via SSL/TLS. Un mécanisme de **throttling** et un **système de notifications** (par UDP ou TCP) informent les participants des événements majeurs (nouvelle équipe, flag soumis avec succès, etc.).


