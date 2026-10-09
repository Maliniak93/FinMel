# syntax=docker/dockerfile:1
# Build context: repository root (same as the service Dockerfiles), so the root .dockerignore applies.

FROM node:26-alpine AS build
WORKDIR /src/web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npx ng build --configuration production

FROM nginx:1-alpine AS runtime
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/web/dist/skarbiec-web/browser /usr/share/nginx/html
EXPOSE 80
