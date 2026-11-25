import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Self-contained server bundle for the Docker image (#20). The Dockerfile copies public/ and .next/static/ into it.
  output: "standalone",

  cacheComponents: true,
  partialPrefetching: true,

  experimental: {
    // Dataset uploads (#15) go through proxy.ts, which otherwise buffers request bodies to ~10 MB.
    proxyClientMaxBodySize: "50mb",
  },

  // Stop `next dev` writing its own block into an AGENTS.md; the repo-root CLAUDE.md/AGENTS.md cover agents.
  agentRules: false,
};

export default nextConfig;
