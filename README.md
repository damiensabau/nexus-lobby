# NEXUS LOBBY

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Licence : PolyForm Noncommercial](https://img.shields.io/badge/licence-PolyForm%20Noncommercial-orange)

🇬🇧 [English version](README.en.md)

**Changeur de région pour le jeu en ligne** : connecte-toi en un clic à un serveur VPN dans un autre pays pour
jouer dans des lobbies souvent plus calmes. L'app note chaque région en direct (heure locale, population de
joueurs, ping) et te propose la meilleure du moment. Pensée pour les jeux de tir en ligne comme *Call of Duty:
Warzone* et le multijoueur de *Call of Duty*.

- 🌍 **Carte du monde** lumineuse : pays coloriés, un repère par ville, drapeaux
- 🏆 **Suggestions en direct** : note sur 100 par région, recalculée chaque minute
- ⚡ **WireGuard et OpenVPN** : importe tes fichiers `.conf` / `.ovpn`, rangés par pays et par ville
- 📈 **Session** : durée, données, débit en direct sur 60 secondes
- 🛡️ **Sûr par défaut** : profils dangereux refusés, tunnel coupé automatiquement si le serveur ne répond pas
- 🔒 **Respect de la vie privée** : aucune télémétrie, ni IP ni ville affichées (seulement le pays visible)

> **Avertissement.** NEXUS LOBBY n'est ni affilié, ni approuvé, ni soutenu par Activision ou un autre éditeur.
> *Call of Duty* et *Warzone* sont des marques d'Activision Publishing, Inc. Changer de région n'apporte **aucune garantie** de lobbies plus faciles : le matchmaking
> dépend surtout de la connexion et du niveau. Utiliser un VPN pour influencer le matchmaking peut enfreindre les
> conditions d'utilisation de certains jeux : **à tes risques**.

## Installation

1. Télécharge `NexusLobby-Setup-x.y.z.exe` dans les [Releases](../../releases) et lance-le (droits admin requis :
   l'app installe un service Windows).
2. Installe [WireGuard](https://www.wireguard.com/install/) et/ou [OpenVPN Community 2.6+](https://openvpn.net/community-downloads/)
   selon tes profils.
3. Dans l'app : **Ajouter une région** → choisis un fichier `.conf` (WireGuard) ou `.ovpn` (OpenVPN), un par pays.

L'app ne fournit pas de serveurs : utilise un fournisseur qui donne des fichiers WireGuard / OpenVPN, ou tes
propres serveurs.

## Comment sont choisies les régions

Chaque région reçoit une note sur 100 (`src/VpnClient.App/Services/LobbyAdvisor.cs`) :

| Critère | Poids | Détail |
|---|---|---|
| Heure locale du serveur | 45 % | Creux vers 3 h–8 h, pic vers 19 h–23 h, heure sur place |
| Population de joueurs du pays | 25 % | Estimation : petite, moyenne ou grande |
| Ping vers le serveur | 20 % | < 80 ms idéal, > 150 ms pénalisé et signalé |
| Jour de la semaine | 10 % | Lundi à jeudi plus calmes que le vendredi soir et le week-end |

Pourquoi : quand il y a peu de joueurs connectés, le matchmaking assouplit ses critères pour garder un temps
d'attente court. Mais les éditeurs privilégient la connexion : un ping trop élevé peut annuler l'effet, voire
te renvoyer dans ta région. **C'est une estimation, pas une garantie.**

La liste est rangée en dossiers (pays, puis ville s'il y a plusieurs serveurs) et triée par défaut du meilleur
score au plus faible ; le bouton de tri bascule entre score, ping et ordre alphabétique.

## Sécurité

Le service tourne avec les droits SYSTEM (il crée les cartes réseau) ; l'interface, elle, tourne sans droits.

- Le canal app ↔ service n'accepte que les sessions ouvertes sur le PC (pas d'accès réseau).
- Les profils importés sont vérifiés : refus des scripts (`up`, `PostUp`…), des DLL (`plugin`, `engine`…) et des
  références à des fichiers du disque (certificats et clés doivent être intégrés au profil).
- OpenVPN est lancé avec `--script-security 1` ; son interface de pilotage est protégée par un mot de passe aléatoire.
- Si un serveur ne répond pas (20 s pour WireGuard, 30 s pour OpenVPN), le tunnel est retiré pour ne pas bloquer ta connexion.
- Les profils sont stockés dans `%ProgramData%\VpnClient\profiles`, accessible à SYSTEM et aux administrateurs uniquement.

Faille trouvée ? Lis [SECURITY.md](SECURITY.md) : merci de ne pas ouvrir d'issue publique.

## Vie privée

Aucune télémétrie. La seule requête vers Internet (hors tunnels) est faite à `ipinfo.io` pour afficher le pays
sous lequel tu es visible ; l'app n'en garde que le code pays. Le ping est mesuré directement vers tes serveurs.

## Développement

Prérequis : Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# Terminal administrateur : le service en console (mode démo actif en développement)
dotnet run --project src/VpnClient.Service

# Second terminal : l'interface
dotnet run --project src/VpnClient.App

# Tests
dotnet test
```

En développement (`appsettings.Development.json`), les profils dont l'identifiant commence par `demo-` simulent
la connexion sans WireGuard ni OpenVPN, et `…-fail-unreachable`, `…-fail-auth`, `…-fail-lost` simulent les pannes.

```
src/
  VpnClient.Core      modèles, codes d'erreur et protocole app ↔ service (JSON sur named pipe)
  VpnClient.Service   service Windows : profils, tunnels WireGuard / OpenVPN
  VpnClient.App       interface WPF (WPF-UI) : carte, suggestions, messages
tests/VpnClient.Tests tests unitaires (xUnit)
installer/            script Inno Setup
scripts/              publication, installation du service en local
```

Publier : `.\scripts\publish.ps1` (fichiers dans `dist\`, plus l'installateur si Inno Setup 6 est installé).
Une version : pousser un tag `vX.Y.Z` lance la construction de l'installateur sur GitHub (Release en brouillon).

Envie de contribuer ? Voir [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE) : utilisation, modification et partage gratuits pour tout usage **non
commercial**. **La vente et tout usage commercial sont interdits** sans accord écrit des auteurs. Ce n'est donc
pas une licence « open source » au sens de l'OSI : le code est public, mais pas réutilisable pour gagner de l'argent.

Les composants tiers gardent leur licence : voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
