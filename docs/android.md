# PROJECT MASS auf Android

PROJECT MASS nutzt auf Android dieselbe Blazor-Oberfläche wie die Web-App. Der Android-WebView hält seine IndexedDB im App-Speicher. Was du in Chrome oder einem anderen Browser löschst, ändert diesen App-Speicher nicht.

## APK installieren

Lade `project-mass.apk` unter **Releases** oder als Artefakt des Workflows **Android** herunter. Entpacke ein gegebenenfalls heruntergeladenes Workflow-ZIP und öffne das APK auf dem Gerät. Erlaube für die App, die das APK öffnet (etwa „Dateien“), die Installation aus unbekannten Quellen. PROJECT MASS unterstützt Android 8 (API 26) und neuer. Aktualisiere bei Bedarf Android System WebView.

## Vorhandene Daten übernehmen und sichern

1. Exportiere in der Web-App unter **Mehr → Daten und Backup** ein JSON-Backup.
2. Übertrage die Datei auf das Android-Gerät und wähle sie in der App unter **Backup einspielen** aus. Prüfe die Vorschau und bestätige.
3. Wähle unter **Automatische Android-Sicherungen → Sicherungsordner wählen** einen beschreibbaren Ordner wie `Dokumente/ProjectMass`.

Die App schreibt automatisch eine Tagesdatei in ihr internes Verzeichnis `backups/`. Im gewählten Ordner aktualisiert sie zusätzlich `project-mass-backup.json` und eine Tagesdatei. Sie behält je Ordner 14 Tagesdateien und sichert auch beim Wechsel in den Hintergrund. Eine aktivierte, verschlüsselte Android-System-Sicherung kann die WebView-Daten und internen Backup-Dateien einbeziehen.

Wenn Android beim ersten Start eine interne Sicherung, aber keine Datenbankdaten wiederhergestellt hat, bietet die App eine Wiederherstellung mit Vorschau an. Nach einer Neuinstallation musst du den externen Sicherungsordner erneut wählen, weil Android die bisherige Ordnerfreigabe beim Deinstallieren entfernt. Bei leerem Datenbestand zeigt die App danach die passende Sicherung an. Deinstallieren oder „App-Daten löschen“ entfernt den internen Speicher.

## Synology per WebDAV

Aktiviere auf der Synology das Paket **WebDAV Server** und HTTPS. Wähle unter **Mehr → Daten und Backup → Mit NAS synchronisieren** WebDAV und gib die HTTPS-Basis-URL, den eingeschränkten NAS-Benutzer und das Passwort ein. Verwende dasselbe übergeordnete NAS-Verzeichnis wie auf dem PC. Der Android-HTTP-Client braucht keine CORS-Freigabe. Die HTTPS-Zertifikatsprüfung bleibt aktiv. [NAS- und Selbsthosting-Anleitung](self-hosting.md)

## Lokal bauen

Voraussetzungen sind das .NET 10 SDK, das Android SDK (API 36) und JDK 21.

```powershell
dotnet workload install maui-android
dotnet build
dotnet test
dotnet publish src/ProjectMass.Android -f net10.0-android -c Release
```

Das signierte APK liegt unter `src/ProjectMass.Android/bin/Release/net10.0-android/publish/`. Zum lokalen Test ohne eigenen Release-Schlüssel nutzt .NET die Debug-Signatur. Die Solution und der schnelle CI-Workflow enthalten Core, Web und Tests. Android wird direkt über seine Projektdatei gebaut.

## Release-Schlüssel einrichten

```powershell
keytool -genkeypair -v -keystore project-mass-release.jks -storetype JKS -alias project-mass -keyalg RSA -keysize 3072 -validity 10000
```

Bewahre die Datei und die Passwörter privat auf. Nutze für Updates auf bereits installierten Geräten denselben Signaturschlüssel und erhöhe `ApplicationVersion` im Android-Projekt für neue Versionen.

Lege unter **Settings → Secrets and variables → Actions** diese Repository-Secrets an:

- `ANDROID_KEYSTORE_BASE64` enthält die Base64-kodierte Keystore-Datei. In PowerShell lässt sie sich mit `[Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path 'project-mass-release.jks').Path))` kodieren.
- `ANDROID_KEYSTORE_PASSWORD` ist das Passwort des Keystores.
- `ANDROID_KEY_ALIAS` ist der Alias, zum Beispiel `project-mass`.
- `ANDROID_KEY_PASSWORD` ist das Passwort des Schlüssels.

Der Workflow hängt bei Tags wie `v2.0.0` das APK an den GitHub Release. Ohne Tag steht es als Workflow-Artefakt bereit. Fehlen die Secrets, warnt der Workflow und baut ein debug-signiertes APK. Android akzeptiert ein mit dem Debug-Schlüssel signiertes APK nur als Update, wenn der bisherige Schlüssel übereinstimmt.
