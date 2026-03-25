FROM node:22-slim AS build
WORKDIR /app

COPY package.json ./
RUN npm install

COPY tsconfig.json ./
COPY src/ src/
RUN npx tsc

# Runtime
FROM node:22-slim
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY package.json ./
RUN npm install --omit=dev

COPY --from=build /app/dist/ dist/

ENV PORT=3000
EXPOSE 3000

CMD ["node", "dist/server.js"]
