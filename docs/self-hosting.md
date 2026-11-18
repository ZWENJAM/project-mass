# PROJECT MASS und WebDAV auf derselben Adresse betreiben

Verwende für die App und WebDAV dieselbe HTTPS-Herkunft. Im Beispiel läuft die App unter `https://mass.example.net/` und WebDAV unter `https://mass.example.net/webdav/`. Die App wird mit der lokalen Basis `/` veröffentlicht.

## CORS für die öffentliche App

Wenn du die App auf GitHub Pages nutzt, konfiguriere auf dem WebDAV-Server oder Reverse Proxy:

- `Access-Control-Allow-Origin: https://zwenjam.github.io`
- `Access-Control-Allow-Methods: GET, PUT, DELETE, PROPFIND, MKCOL, OPTIONS`
- `Access-Control-Allow-Headers: Authorization, Content-Type, Depth, If-Match, If-None-Match`
- `Access-Control-Expose-Headers: ETag`

Beantworte den Browser-Preflight (`OPTIONS`) ohne HTTP-Anmeldung. Die eigentlichen WebDAV-Anfragen erfordern weiterhin die Zugangsdaten. Gib die CORS-Header auch bei Fehlermeldungen aus, damit die App die HTTP-Fehler anzeigen kann. Die WebDAV-Basis-URL muss direkt auf die endgültige HTTPS-Adresse zeigen. Ein Browser kann für ein NAS im lokalen Netz zusätzlich eine Netzwerkfreigabe verlangen.

## Statische App veröffentlichen

```powershell
dotnet publish src/ProjectMass -c Release -o publish
```

Kopiere den Inhalt von `publish/wwwroot` in das Verzeichnis für die statischen Dateien. Für einen Container mit der statischen App:

```powershell
docker build -t project-mass .
docker run --rm -p 127.0.0.1:8080:80 project-mass
```

Das Image liefert die statischen Dateien und den SPA-Fallback. Leite dafür am HTTPS-Reverse-Proxy die App-Anfragen an den Container weiter und `/webdav/` an den WebDAV-Server. Bei einer nginx-Installation mit direkt bereitgestellten Dateien sieht die Server-Konfiguration beispielsweise so aus:

```nginx
server {
    listen 443 ssl;
    server_name mass.example.net;
    root /srv/project-mass/wwwroot;
    ssl_certificate /etc/nginx/tls/fullchain.pem;
    ssl_certificate_key /etc/nginx/tls/privkey.pem;

    location /webdav/ {
        # Der Schrägstrich am Ende gibt den Pfad hinter /webdav/ an das NAS weiter.
        proxy_pass https://nas.internal.example:5006/;
        proxy_ssl_server_name on;
        proxy_ssl_verify on;
        proxy_ssl_trusted_certificate /etc/nginx/tls/nas-ca.pem;
        proxy_set_header Host $http_host;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header Authorization $http_authorization;
        client_max_body_size 64m;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
```

Passe Hostnamen, Zertifikate und gegebenenfalls den WebDAV-Pfad an. Bei einer Container-Installation kannst du in `location /` stattdessen `proxy_pass http://127.0.0.1:8080;` verwenden. Führe vor dem Neuladen der nginx-Konfiguration `nginx -t` aus.

Öffne die App über ihre öffentliche oder interne **HTTPS-Adresse**. Wenn dein WebDAV-Freigabeordner etwa `home` heißt, verwende im Beispiel `https://mass.example.net/webdav/home/` als Basis-URL. Gib dem Sync-Benutzer nur die benötigten Rechte in dieser Freigabe. Auf demselben Ursprung braucht die App keine CORS-Freigabe.
