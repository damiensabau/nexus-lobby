# Composants tiers / Third-party notices

NEXUS LOBBY est distribué sous [PolyForm Noncommercial 1.0.0](LICENSE). Les composants ci-dessous gardent leur
propre licence ; elles s'appliquent à ces composants uniquement.

## Bibliothèques incluses dans l'application

| Composant | Licence | Source |
|---|---|---|
| WPF-UI 4.3.0 (+ WPF-UI.Abstractions) | MIT | https://github.com/lepoco/wpfui |
| CommunityToolkit.Mvvm 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.Hosting / .WindowsServices 10.0 | MIT | https://github.com/dotnet/runtime |
| System.ServiceProcess.ServiceController 10.0 | MIT | https://github.com/dotnet/runtime |
| .NET 10 (runtime inclus dans l'installateur) | MIT | https://github.com/dotnet/runtime |

Le texte de la licence MIT est disponible dans chaque dépôt cité.

## Données

### Carte du monde — Natural Earth, via world-atlas

`src/VpnClient.App/Resources/WorldMap.xaml` est généré (projection équirectangulaire) à partir de
`countries-110m.json` du paquet [world-atlas 2.0.2](https://github.com/topojson/world-atlas), redistribution des
données [Natural Earth](https://www.naturalearthdata.com/) 1:110m (domaine public). Script de génération :
`tools/map/topo2xaml.py`.

Licence de world-atlas (ISC) :

```
Copyright 2013-2019 Michael Bostock

Permission to use, copy, modify, and/or distribute this software for any purpose
with or without fee is hereby granted, provided that the above copyright notice
and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH
REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT,
INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS
OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER
TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF
THIS SOFTWARE.
```

### Drapeaux — Flagpedia / flagcdn

`src/VpnClient.App/Resources/Flags/*.png` : images de [flagcdn.com](https://flagcdn.com), service de
[Flagpedia.net](https://flagpedia.net), basées sur les fichiers vectoriels de Wikimedia Commons. Flagpedia indique
que les drapeaux sont du domaine public, utilisables gratuitement pour un usage commercial ou non.

## Logiciels lancés mais non inclus

L'app **lance** les versions installées séparément par l'utilisateur ; elle ne les contient pas et ne les modifie pas :

- [WireGuard for Windows](https://www.wireguard.com/) — WireGuard est une marque déposée de Jason A. Donenfeld.
- [OpenVPN Community](https://openvpn.net/community/) — OpenVPN est une marque déposée d'OpenVPN Inc.

## Marques

*Call of Duty* et *Warzone* sont des marques d'Activision Publishing, Inc. NEXUS LOBBY n'est ni affilié, ni
approuvé, ni soutenu par Activision.
