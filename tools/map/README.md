# Génération de la carte

`src/VpnClient.App/Resources/WorldMap.xaml` est généré à partir des données Natural Earth 1:110m
(paquet world-atlas, licence ISC — voir `THIRD-PARTY-NOTICES.md`).

```powershell
curl.exe -L -o countries-110m.json https://cdn.jsdelivr.net/npm/world-atlas@2.0.2/countries-110m.json
python topo2xaml.py countries-110m.json ..\..\src\VpnClient.App\Resources\WorldMap.xaml
```

Le script produit une géométrie pour le monde entier (`WorldMapGeometry`) et une par pays du catalogue
(`Country.XX`, code ISO), utilisée pour colorier les pays où tu as un serveur. Pour un nouveau pays, ajoute son nom
Natural Earth et son code dans `CODES`.

La projection (équirectangulaire 1000 × 394,4, latitudes 84° à −58°) doit rester identique à celle de
`Location.MapX` / `Location.MapY` dans `src/VpnClient.App/Services/Locations.cs`.
