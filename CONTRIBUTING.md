# Contribuer

Merci de ton intérêt ! Les contributions sont les bienvenues : corrections, nouvelles régions, traductions, idées.

## Avant de commencer

- Pour un bug ou une idée, ouvre d'abord une **issue** (modèles fournis).
- Pour une **faille de sécurité**, ne passe pas par une issue : voir [SECURITY.md](SECURITY.md).
- En contribuant, tu acceptes que ton code soit publié sous la licence du projet,
  [PolyForm Noncommercial 1.0.0](LICENSE).

## Mettre en place le projet

```powershell
git clone <url-du-depot>
cd <dossier>
dotnet test                                        # 90+ tests, doivent tous passer
dotnet run --project src/VpnClient.Service         # terminal administrateur
dotnet run --project src/VpnClient.App             # second terminal
```

Le mode démo (actif en développement) simule les connexions pour les profils `demo-…` : pas besoin de vrais serveurs
pour travailler sur l'interface.

## Règles

- **Ne commite jamais de vrai profil VPN** (`.conf`, `.ovpn`) : ils contiennent des clés privées. Le `.gitignore`
  les bloque ; les tests utilisent des profils écrits dans le code avec des clés factices.
- Garde le style du code existant : commentaires en français, textes de l'interface au tutoiement.
- Toute logique nouvelle (notation, validation, tri…) vient avec ses **tests** dans `tests/VpnClient.Tests`.
- Toute modification de `ProfileStore` (validation des profils) doit garder ou renforcer la sécurité, avec des tests
  pour les cas refusés.
- La compilation doit passer sans avertissement : `dotnet build -c Release -warnaserror`.

## Ajouter une région au catalogue

1. Ajoute une ligne dans `src/VpnClient.App/Services/Locations.cs` (identifiant `pays-ville`, coordonnées,
   fuseau horaire IANA).
2. Si le pays est nouveau : ajoute son drapeau `xx.png` (160 px de large, flagcdn) dans
   `src/VpnClient.App/Resources/Flags`, sa population estimée dans `LobbyAdvisor.cs`, et son nom dans le script
   `tools/map/topo2xaml.py` pour le surlignage sur la carte.
3. `dotnet test` vérifie fuseaux horaires, drapeaux, coordonnées et carte.

## Pull requests

- Une PR = un sujet. Décris ce qui change et comment tu l'as testé (captures bienvenues pour l'interface).
- La CI (compilation + tests) doit être verte.
