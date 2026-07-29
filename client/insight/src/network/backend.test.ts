import { User } from "oidc-client-ts";
import { afterEach, expect, test, vi } from "vitest";
import { authenticatedFetch, setUserToken } from "./backend.js";

afterEach(() => vi.restoreAllMocks());

test("authenticated fetch preserves request headers while adding the bearer token", async () => {
  const fetch = vi.spyOn(window, "fetch").mockResolvedValue(new Response());
  const request = new Request("https://example.test/api", {
    headers: { "X-Insight-Client": "extension" },
  });
  setUserToken(
    new User({
      access_token: "access-token",
      token_type: "Bearer",
      profile: { iss: "issuer", aud: "client", exp: 0, iat: 0, sub: "subject" },
    }),
  );

  await authenticatedFetch(request);

  const [input, init] = fetch.mock.calls[0]!;
  const headers = new Headers(init?.headers);
  expect(input).toBe(request);
  expect(headers.get("X-Insight-Client")).toBe("extension");
  expect(headers.get("Authorization")).toBe("Bearer access-token");
});
