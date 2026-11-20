import { afterEach, describe, expect, it, vi } from "vitest";

import { GET } from "./route";

afterEach(() => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
});

describe("GET /api/health", () => {
  it("includes the API's health when it's up", async () => {
    vi.stubEnv("HELIX_VERSION", "sha-abc1234");
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify({ status: "Healthy", version: "dev" }), {
        status: 200,
      }),
    );

    const response = await GET();

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({
      status: "ok",
      version: "sha-abc1234",
      api: { status: "Healthy", version: "dev" },
    });
  });

  it("reports api as null when the API is unreachable", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new Error("fetch failed"));

    const response = await GET();

    expect(response.status).toBe(200);
    expect((await response.json()).api).toBeNull();
  });

  it("falls back to dev when HELIX_VERSION isn't set", async () => {
    vi.stubEnv("HELIX_VERSION", undefined);
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new Error("fetch failed"));

    const response = await GET();

    expect((await response.json()).version).toBe("dev");
  });
});
