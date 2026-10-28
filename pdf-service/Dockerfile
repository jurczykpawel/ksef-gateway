FROM node:26-slim@sha256:94816674c62774da0d099a947bf4613b9e1972a2733abd0d0cd80e795f8411d0
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
