FROM node:26-slim@sha256:5adad64f9a1fca2d51972caa65983634c8a774eb300d72b20f7f2a0f60368072
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Install app dependencies
COPY package.json package-lock.json ./
RUN npm ci --omit=dev

# Copy CIRFMF pdf-generator library source
COPY lib/src/ lib/src/
COPY lib/package.json lib/package.json
COPY lib/LICENSE /app/licenses/ksef-pdf-generator-LICENSE
COPY LICENSE THIRD_PARTY_NOTICES.md /app/licenses/

# Copy app source
COPY src/ src/
COPY tsconfig.json ./

ENV NODE_ENV=production
ENV PORT=3000
USER node
EXPOSE 3000

# Use tsx for TypeScript runtime (handles imports from lib/src/)
CMD ["node", "--import", "tsx", "src/server.ts"]
