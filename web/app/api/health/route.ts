export const dynamic = "force-dynamic";

/**
 * The API's health, fetched with a short timeout so a stuck or unreachable API doesn't hold this route up.
 * @returns the parsed JSON body, or null when the request fails or times out
 */
async function fetchApiHealth(): Promise<unknown> {
  try {
    const response = await fetch(`${process.env.API_URL}/health`, {
      signal: AbortSignal.timeout(3000),
    });
    return await response.json();
  } catch {
    return null;
  }
}

/**
 * Public health check, polled by the deploy workflow to confirm a rollout reached the expected version. Always
 * answers 200; the API's reachability and version are reported in the body instead of the status code.
 * @returns `{ status: "ok", version, api }`
 */
export async function GET(): Promise<Response> {
  const api = await fetchApiHealth();

  return Response.json({
    status: "ok",
    version: process.env.HELIX_VERSION ?? "dev",
    api,
  });
}
