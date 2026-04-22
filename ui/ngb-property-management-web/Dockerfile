# syntax=docker/dockerfile:1.7

FROM node:22.17-alpine AS build
WORKDIR /workspace

COPY package.json package-lock.json ./
COPY ngb-ui-framework/package.json ngb-ui-framework/package.json
COPY ngb-property-management-web/package.json ngb-property-management-web/package.json

RUN --mount=type=cache,target=/root/.npm npm ci

COPY ngb-ui-framework ngb-ui-framework
COPY ngb-property-management-web ngb-property-management-web
COPY postcss.shared.config.js tailwind.shared.config.js ./

RUN npm run build:pm-web

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
USER root

RUN apk add --no-cache gettext

COPY ngb-property-management-web/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY ngb-property-management-web/docker/runtime-config.js.template /etc/nginx/templates/runtime-config.js.template
COPY ngb-property-management-web/docker/entrypoint.sh /docker-entrypoint-custom.sh
COPY --from=build /workspace/ngb-property-management-web/dist /usr/share/nginx/html

RUN chmod +x /docker-entrypoint-custom.sh \
  && chown -R nginx:nginx /usr/share/nginx/html /etc/nginx/conf.d /etc/nginx/templates /docker-entrypoint-custom.sh

USER nginx

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["/docker-entrypoint-custom.sh"]
