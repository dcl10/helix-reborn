import { describe, expect, it } from "vitest";

import { buildKeycloakLogoutUrl } from "../sign-out";

const issuer = "http://localhost:8080/realms/helix";

describe("buildKeycloakLogoutUrl", () => {
  it("builds the end-session URL with the hint, client and return URL", () => {
    const url = buildKeycloakLogoutUrl({
      issuer,
      clientId: "helix-web",
      idTokenHint: "id-token",
      postLogoutRedirectUri: "http://localhost:3000/",
    });

    expect(url?.origin).toBe("http://localhost:8080");
    expect(url?.pathname).toBe(
      "/realms/helix/protocol/openid-connect/logout",
    );
    expect(Object.fromEntries(url?.searchParams ?? [])).toEqual({
      id_token_hint: "id-token",
      client_id: "helix-web",
      post_logout_redirect_uri: "http://localhost:3000/",
    });
  });

  it("handles an issuer with a trailing slash", () => {
    expect(buildKeycloakLogoutUrl({ issuer: `${issuer}/` })?.toString()).toBe(
      `${issuer}/protocol/openid-connect/logout`,
    );
  });

  it("leaves out parameters it wasn't given", () => {
    const url = buildKeycloakLogoutUrl({ issuer, clientId: "helix-web" });

    expect(url?.searchParams.has("id_token_hint")).toBe(false);
    expect(url?.searchParams.has("post_logout_redirect_uri")).toBe(false);
    expect(url?.searchParams.get("client_id")).toBe("helix-web");
  });

  it("returns null when there is no issuer", () => {
    expect(buildKeycloakLogoutUrl({ clientId: "helix-web" })).toBeNull();
  });
});
