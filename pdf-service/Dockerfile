FROM node:22-slim@sha256:de170b39439eb0e5a3444ad7d60b015614a381e10fecae2e30ec9e735122f38d
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Install app dependencies
COPY package.json package-lock.json ./
RUN npm ci --omit=dev

# Copy CIRFMF pdf-generator library source
COPY lib/src/ lib/src/
COPY lib/LICENSE /app/licenses/ksef-pdf-generator.LICENSE
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
