# Zu PROJECT MASS beitragen

Installiere das .NET 10 SDK, klone das Repository und wechsle in den Projektordner.

```powershell
git clone https://github.com/ZWENJAM/project-mass.git
cd project-mass
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
dotnet run --project src/ProjectMass
```

## Code-Stil

- Geschäftslogik gehört in die Services. Decke neue Berechnungen und Änderungen am Datenverhalten mit xUnit-Tests ab.
- Richte neue Oberflächen nach [DESIGN.md](DESIGN.md) aus. Verwende deutsche Texte für Nutzerinnen und Nutzer.
- Prüfe bei UI-Änderungen Mobil- und Desktopbreiten sowie die passenden Farbmodi.

## Pull Requests

1. Beschreibe größere Änderungen zuerst in einem Issue. Erstelle für den Pull Request einen eigenen Branch.
2. Halte die Änderung fokussiert und beschreibe das Ergebnis und die durchgeführten Prüfungen. Bei UI-Änderungen helfen Screenshots mit Demodaten.
3. Öffne den Pull Request gegen `main`. Der Release-Build muss warnungsfrei sein und alle Tests müssen bestehen.

Verwende fiktive Beispiele. Persönliche Daten, private Backups und API-Keys gehören weder in Commits noch in Issues.
