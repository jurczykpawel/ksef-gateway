FROM node:22-slim
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Install app dependencies
COPY package.json package-lock.json ./
RUN npm ci --omit=dev

# Copy CIRFMF pdf-generator library source
COPY lib/src/ lib/src/

# Copy app source
COPY src/ src/
COPY tsconfig.json ./

ENV NODE_ENV=production
ENV PORT=3000
USER node
EXPOSE 3000

# Use tsx for TypeScript runtime (handles imports from lib/src/)
CMD ["node", "--import", "tsx", "src/server.ts"]
