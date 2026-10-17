# Logo et icônes

`source.png` est le logo d'origine (fond noir). `Program.cs` en tire :

- un PNG à **fond transparent** qui garde la lueur : la luminosité de chaque pixel devient sa transparence,
  puis la couleur est « dé-prémultipliée » (le noir disparaît, le halo reste bleu au lieu de virer au gris) ;
- un recadrage carré centré sur le logo ;
- `logo-1024/512/256.png` et `app.ico` (PNG 256, 128, 64, 48, 32, 24 et 16 px).

```powershell
dotnet run --project tools/logo -- tools/logo/source.png <dossier-sortie>
```

Puis copier `logo-256.png` → `src/VpnClient.App/Resources/logo.png`, `app.ico` → `src/VpnClient.App/Resources/app.ico`
et `logo-512.png` → `docs/logo.png`.

L'icône de la zone de notification est dessinée à l'exécution (`TrayIcon.cs`) : le logo + une pastille d'état.
