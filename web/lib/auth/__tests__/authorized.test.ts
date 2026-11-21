import type { Session } from "next-auth";
import { describe, expect, it } from "vitest";

import { authorizeRequest } from "../authorized";

const session: Session = {
  user: { id: "user-1", name: "Ada" },
  expires: "2026-12-01T00:00:00.000Z",
};

function authorize(path: string, auth: Session | null) {
  return authorizeRequest({
    auth,
    request: { nextUrl: new URL(path, "http://localhost:3000") },
  });
}

describe("authorizeRequest", () => {
  it("lets a signed-in user through", () => {
    expect(authorize("/experiments", session)).toBe(true);
    expect(authorize("/api/proxy/me", session)).toBe(true);
  });

  it.each(["/", "/sign-in", "/api/health"])(
    "always lets %s through without a session",
    (path) => {
      expect(authorize(path, null)).toBe(true);
    },
  );

  it("sends a signed-out user to sign in", () => {
    expect(authorize("/experiments", null)).toBe(false);
  });

  it("treats a session whose refresh failed as signed out", () => {
    expect(
      authorize("/experiments", {
        ...session,
        error: "RefreshAccessTokenError",
      }),
    ).toBe(false);
  });

  it("answers 401 on the API proxy instead of redirecting", async () => {
    const result = authorize("/api/proxy/me", null);

    expect(result).toBeInstanceOf(Response);
    expect((result as Response).status).toBe(401);
    expect(await (result as Response).json()).toEqual({
      error: "Not signed in",
    });
  });
});
