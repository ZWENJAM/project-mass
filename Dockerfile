FROM nginx:alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY publish/wwwroot/ /usr/share/nginx/html/
