FROM node:24.19.0 AS build
WORKDIR /app
COPY package.json package-lock.json ./
COPY .ngb-packages/ /tmp/ngb-packages/
RUN if [ -f /tmp/ngb-packages/ui.tgz ]; then npm cache add /tmp/ngb-packages/ui.tgz; fi \
    && npm ci --workspaces=false
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
