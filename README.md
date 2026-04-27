# Fix72 Agent — Prototype MVP

Application de surveillance bienveillante du PC client, développée pour **Fix72 — Etienne Aubry** (dépannage informatique, Le Mans / Sarthe).

Voir [`cahier des cahrges.txt`](cahier%20des%20cahrges.txt) pour la spécification fonctionnelle complète.

## Périmètre du MVP (cette version 0.1)

- Icône systray à 3 états (vert / orange / rouge)
- Menu clic-droit : voir l'état, appeler Etienne, message WhatsApp, téléassistance (placeholder), paramètres, quitter
- Tableau de bord : grille de tuiles colorées
- Surveillance : **disque dur**, **mémoire RAM**, **mises à jour Windows**
- Notifications Windows (balloon tips) avec anti-spam et heures de silence
- Démarrage automatique avec Windows (clé registre HKCU)
- Instance unique (mutex global)
- Installateur Inno Setup
- Script de build automatisé

## Hors périmètre (à venir)

- Surveillance température / SMART / antivirus / démarrage lent
- Téléassistance AnyDesk en 1 clic (Phase 2)
- Mise à jour automatique (Phase 2)
- Fenêtre de paramètres (à ce stade : édition manuelle de `settings.json`)
- Toast notifications Windows 11 modernes (à la place : balloon tips classiques)
- Signature de code

---

## Compiler le projet

### Prérequis

| Outil | Version | Lien |
|---|---|---|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Inno Setup (optionnel — pour l'installateur) | 6+ | https://jrsoftware.org/isdl.php |

### Build rapide

```powershell
.\build.ps1
```

Cela produit :
- `Fix72Agent\bin\Release\net8.0-windows\win-x64\publish\Fix72Agent.exe` — binaire autonome (~75 Mo, .NET 8 inclus)
- `dist\Fix72Agent-setup-0.1.0.exe` — installateur (si Inno Setup est détecté)

### Build manuel

```powershell
cd Fix72Agent
dotnet build -c Release
```

Pour lancer en debug :

```powershell
dotnet run -c Debug
```

---

## Installation manuelle (sans installateur)

1. Compiler avec `build.ps1`.
2. Copier le `Fix72Agent.exe` dans `C:\Program Files\Fix72Agent\`.
3. Lancer `Fix72Agent.exe` — l'icône apparaît dans la zone de notification.
4. Le démarrage automatique avec Windows est activé par défaut (clé `HKCU\...\Run`).

---

## Fichier de configuration

À l'installation (et à chaque démarrage), un fichier de réglages est lu/créé ici :

```
%APPDATA%\Fix72Agent\settings.json
```

Exemple complet :

```json
{
  "ClientName": "Mme Dupont",
  "StartWithWindows": true,
  "NotificationsEnabled": true,
  "QuietHoursEnabled": true,
  "CheckIntervalMinutes": 15,
  "TechnicianPhone": "06 64 31 34 74",
  "ClientId": "",
  "WebhookUrl": "https://hook.eu2.make.com/abc123…",
  "DailyReportEnabled": true,
  "DailyReportHour": 9,
  "OnlySendDailyWhenAlerts": true,
  "LastReportSent": "",
  "WeeklyHeartbeatEnabled": true,
  "LastHeartbeatSent": "",
  "ImmediateCriticalAlertsEnabled": true
}
```

Personnalisation actuelle : éditer ce fichier directement, puis redémarrer l'application via le menu systray.

---

## 📧 Envoi de rapports à Fix72 (via Make.com)

L'agent peut envoyer 4 types d'événements à un webhook (typiquement **Make.com** ou Zapier) :

| Événement | Quand | Coût en ops Make |
|---|---|---|
| `critical_alert` | Dès qu'une nouvelle alerte rouge apparaît (disque <5%, MAJ critique, SMART…) | 2 ops par incident, ~30/mois pour 50 clients |
| `daily_report` | Une fois par jour à `DailyReportHour` **uniquement s'il y a au moins une alerte** (configurable) | 2 ops, ~150/mois pour 50 clients |
| `heartbeat` | Une fois tous les 7 jours, indépendant de l'état (preuve que l'agent tourne toujours) | 2 ops × 4 sem × 50 clients = ~400/mois |
| `manual_report` | Quand le client clique sur "Envoyer un rapport à Fix72" | 2 ops par clic |

**Total estimé pour 50 clients : ~780 ops/mois → tient dans le plan Make Free (1000 ops/mois).**

Pour passer à 100+ clients sans payer, désactive le `WeeklyHeartbeatEnabled` (tu perds la détection des agents tombés mais tu divises encore par 2).

### Setup Make.com — étape par étape

1. Crée un compte gratuit sur https://www.make.com (Plan Free : 1000 ops/mois, largement suffisant).
2. **Create a new scenario**.
3. Premier module → cherche **"Webhooks"** → **"Custom webhook"** → **Add** → donne-lui un nom (ex: `fix72-agent`) → **Save**.
4. Copie l'URL générée (du type `https://hook.eu2.make.com/xxxxx`).
5. Sur le PC client, édite `%APPDATA%\Fix72Agent\settings.json` :
   - Renseigne `ClientName` (ex: `"Mme Dupont"`)
   - Colle l'URL dans `WebhookUrl`
6. Redémarre Fix72 Agent (clic droit systray → Quitter, puis relance le `.exe`).
7. Clic droit systray → **"Envoyer un rapport à Fix72"** → Make.com reçoit le 1er payload et auto-détecte la structure.
8. Dans Make.com, ajoute un 2e module → **"Email > Send an email"** (ou **"Gmail > Send an email"** si tu connectes ton compte Gmail).
9. Dans le destinataire : `etienne06080608@gmail.com`. Dans le sujet : `Fix72 — {{1.summary.headline}} ({{1.client.name}})`. Dans le corps : utilise les champs Make pour formatter le message.
10. **Active le scenario** (bouton ON en bas).

### Routage par type d'événement (avancé)

Pour traiter différemment les 3 types, ajoute un **Router** après le webhook :

- Branche 1 — filtre `event_type = critical_alert` → email avec sujet `🔴 URGENT` + envoi SMS via Twilio
- Branche 2 — filtre `event_type = daily_report` → email synthèse
- Branche 3 — filtre `event_type = manual_report` → email + ajoute une tâche dans Trello / Notion

### Format du payload reçu

```json
{
  "event_type": "critical_alert",
  "timestamp": "2026-04-27T18:32:14+02:00",
  "client": {
    "name": "Mme Dupont",
    "id": "",
    "computer": "DESKTOP-ABC123",
    "user": "Marie",
    "phone": "06 64 31 34 74"
  },
  "summary": {
    "overall_level": "critical",
    "alert_count": 1,
    "critical_count": 1,
    "headline": "🔴 ALERTE CRITIQUE — Disque dur : Plein"
  },
  "triggered_by": {
    "id": "disk", "name": "Disque dur", "level": "critical",
    "status": "Plein", "detail": "2 Go libres sur C:",
    "message": "Votre disque C: est presque plein…"
  },
  "monitors": [ … état de tous les capteurs … ],
  "agent_version": "0.1.0"
}
```

### Test rapide sans client

1. Édite `settings.json` sur ton propre PC, mets ton `WebhookUrl`.
2. Force une alerte rouge en remplissant manuellement un disque ou en désactivant le check Online de Windows Update.
3. Au prochain check (15 min ou redémarrage), tu reçois le mail.

⚠️ **Sécurité** : le `WebhookUrl` Make.com **n'est pas un secret** — c'est une URL avec un token aléatoire que tu peux régénérer/désactiver à tout moment depuis Make.com. C'est pour ça qu'on n'a pas mis tes identifiants Gmail dans l'app.

---

## Architecture

```
Fix72Agent/
├── Program.cs                 # Entrée + mutex instance unique
├── TrayApplication.cs         # NotifyIcon, menu, états visuels, timer
├── MainDashboard.cs           # Fenêtre tableau de bord
├── CallDialog.cs              # Popup "appeler" (numéro en grand + copier)
├── IconFactory.cs             # Génère les icônes vert/orange/rouge à la volée
├── Models/
│   ├── AppSettings.cs
│   └── MonitorResult.cs
├── Monitors/
│   ├── IMonitor.cs
│   ├── DiskMonitor.cs         # System.IO.DriveInfo
│   ├── RamMonitor.cs          # P/Invoke GlobalMemoryStatusEx
│   └── WindowsUpdateMonitor.cs # COM late-binding sur Microsoft.Update.Session
├── Services/
│   ├── SettingsService.cs     # JSON dans %APPDATA%
│   └── AutoStartService.cs    # Clé registre HKCU\...\Run
├── Setup/
│   └── Fix72Agent.iss         # Script Inno Setup
└── app.manifest               # DPI awareness, requestedExecutionLevel
```

Aucune dépendance NuGet externe — tout repose sur le SDK .NET 8 / WinForms et l'API Windows native.

---

## Personnalisation rapide

### Changer le numéro de téléphone affiché

Éditer `%APPDATA%\Fix72Agent\settings.json` → champ `TechnicianPhone`.

### Changer le logo / les couleurs

`IconFactory.cs` — la fonction `CreateShieldIcon` dessine le bouclier. Pour passer à un vrai logo, remplacer `Icon.FromHandle(bmp.GetHicon())` par un chargement depuis un fichier `.ico` embarqué.

### Changer la fréquence de vérification

`%APPDATA%\Fix72Agent\settings.json` → champ `CheckIntervalMinutes` (par défaut : 15).

---

## Dépannage

**L'icône systray ne s'affiche pas** → vérifier que la zone de notification Windows n'est pas masquée (paramètres barre des tâches → "Sélectionner les icônes…").

**La vérification Windows Update échoue** → l'API COM `Microsoft.Update.Session` peut être bloquée par certaines politiques d'entreprise. La tuile passe alors en état "❓ Vérification impossible" sans planter.

**SmartScreen bloque l'installateur** → normal en phase MVP (binaire non signé). Cliquer "Informations complémentaires" → "Exécuter quand même". Voir la section signature de code dans le cahier des charges pour la résolution définitive.

---

## Critères de recette MVP

- [ ] Compile sans erreur avec `.\build.ps1`
- [ ] Icône systray visible au démarrage
- [ ] Tableau de bord s'ouvre au double-clic, affiche 3 tuiles (disque / RAM / mises à jour)
- [ ] Bouton "Appeler" affiche le numéro en grand
- [ ] "Démarrer avec Windows" coché par défaut, désactivable via `settings.json`
- [ ] Une seule instance possible (deuxième lancement = silencieux)
- [ ] Désinstallation propre via Panneau de configuration

---

Fix72 — Etienne Aubry — fix72.com — 06 64 31 34 74
Prototype v0.1 — Avril 2026
