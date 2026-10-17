# Politique de sécurité / Security policy

NEXUS LOBBY installe un service Windows qui tourne avec les droits **SYSTEM**. Une faille peut donc permettre une
élévation de privilèges : merci de la signaler **en privé**.

## Signaler une faille

- Utilise **« Report a vulnerability »** dans l'onglet *Security* du dépôt GitHub (GitHub Security Advisories).
- **N'ouvre pas d'issue publique** et ne publie pas de preuve de concept avant qu'un correctif soit disponible.
- Décris la version, les étapes pour reproduire et l'impact (élévation de privilèges, fuite de clés, contournement
  de la validation des profils…).

Tu recevras une réponse sous 7 jours. Une fois corrigée, la faille est publiée avec un crédit pour toi (si tu le souhaites).

## Versions prises en charge

Seule la dernière version publiée reçoit des correctifs de sécurité.

## Périmètre

Sont notamment concernés :

- le canal `\\.\pipe\VpnClient.Service` (accès, contrôle des requêtes) ;
- la validation des profils importés (`src/VpnClient.Service/ProfileStore.cs`) : scripts, DLL, lecture ou
  écriture de fichiers avec les droits SYSTEM ;
- le stockage des profils (`%ProgramData%\VpnClient`) et des identifiants ;
- le lancement de `wireguard.exe` / `openvpn.exe` et l'interface de management d'OpenVPN.

Hors périmètre : les failles de WireGuard ou d'OpenVPN eux-mêmes (à signaler à leurs projets), et les actions
nécessitant déjà les droits administrateur.

---

**English:** please report vulnerabilities privately via GitHub's *Report a vulnerability* button (Security tab),
not in public issues. You'll get an answer within 7 days. Only the latest release is supported.
