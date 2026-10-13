# syntax=docker/dockerfile:1
# Build context: repository root (same as the service Dockerfiles).

FROM node:26-alpine
WORKDIR /app
COPY e2e/stub/server.mjs ./
COPY e2e/market-data/ ./market-data/
USER node
EXPOSE 8080
CMD ["node", "server.mjs"]
