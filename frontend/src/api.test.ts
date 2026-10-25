/// <reference types="vite/client" />
import { afterEach, expect, it, vi } from "vitest";
import { apiGet } from "./api/client";

// A criterion's test carries its id in its name: it("… [AC R-001.AC-1]", …).
const HEALTHY = { status: "Healthy", checks: [{ name: "database", status: "Healthy" }] };
const PROBLEM = {
  type: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  title: "An error occurred while processing your request.",
  status: 500,
  correlationId: "0HN7Q2J1K3L4M",
};

afterEach(() => {
  vi.unstubAllGlobals();
});

function stubFetch(...responses: Array<() => Response>) {
  const fetchMock = vi.fn<typeof fetch>();
  for (const respond of responses) {
    fetchMock.mockImplementationOnce(async () => respond());
  }
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function jsonResponse(body: unknown, status: number, contentType: string) {
  return () => new Response(JSON.stringify(body), { status, headers: { "Content-Type": contentType } });
}

function requestedPath(fetchMock: ReturnType<typeof stubFetch>) {
  const input = fetchMock.mock.calls[0][0];
  const url = input instanceof Request ? input.url : String(input);
  return new URL(url, window.location.origin).pathname;
}

it("returns the problem's status, title and correlation id as a failure [AC R-009.AC-7]", async () => {
  stubFetch(jsonResponse(PROBLEM, 500, "application/problem+json"));

  const result = await apiGet("/health");

  expect(result).toMatchObject({
    ok: false,
    status: PROBLEM.status,
    title: PROBLEM.title,
    correlationId: PROBLEM.correlationId,
  });
  expect(result).not.toHaveProperty("data");
});

it("returns a 200 body typed by the generated schema [AC R-009.AC-7]", async () => {
  const fetchMock = stubFetch(jsonResponse(HEALTHY, 200, "application/json"));

  const result = await apiGet("/health");

  expect(requestedPath(fetchMock)).toBe("/health");
  expect(result).toEqual({ ok: true, data: HEALTHY });
  const status: string | undefined = result.ok ? result.data.status : undefined;
  expect(status).toBe("Healthy");
});

it("returns a failure without a correlation id when the network fails [AC R-009.AC-8]", async () => {
  const fetchMock = vi.fn<typeof fetch>().mockRejectedValue(new TypeError("Failed to fetch"));
  vi.stubGlobal("fetch", fetchMock);

  const result = await apiGet("/health");

  expect(fetchMock).toHaveBeenCalledOnce();
  expect(result.ok).toBe(false);
  expect(result).not.toHaveProperty("data");
  expect(result.ok ? "succeeded" : result.correlationId).toBeUndefined();
});

it.each([502, 200])("returns a failure for a non-JSON body with status %i [AC R-009.AC-8]", async (status) => {
  const fetchMock = stubFetch(
    () => new Response("<html><body>Bad gateway</body></html>", { status, headers: { "Content-Type": "text/html" } }),
  );

  const result = await apiGet("/health");

  expect(fetchMock).toHaveBeenCalledOnce();
  expect(result.ok).toBe(false);
  expect(result).not.toHaveProperty("data");
  expect(result.ok ? "succeeded" : result.correlationId).toBeUndefined();
});

it("generates the client's paths from the back end's OpenAPI document [AC R-009.AC-11]", async () => {
  const { default: schema } = await import("./api/schema.d.ts?raw");

  expect(schema).toContain("export interface paths");
  expect(schema).toContain('"/health"');
});

it("does not compile a call to a path the OpenAPI document does not list [AC R-009.AC-11]", async () => {
  stubFetch(jsonResponse({ ...PROBLEM, title: "Not Found", status: 404 }, 404, "application/problem+json"));

  // `npm run typecheck` fails if this call compiles: the directive is then unused (TS2578).
  // @ts-expect-error -- "/no-such-path" is not a path of the generated schema.
  const result = await (await import("./api/client")).apiGet("/no-such-path");

  expect(result.ok).toBe(false);
});

it("names the command that regenerates the client's types [AC R-009.AC-12]", async () => {
  const { default: packageJson } = await import("../package.json?raw");
  const { default: readme } = await import("../../README.md?raw");

  const generate: string = JSON.parse(packageJson).scripts["generate:api"];
  expect(generate).toContain("openapi-typescript");
  expect(generate).toContain("/api/openapi.json");
  expect(generate).toContain("src/api/schema.d.ts");
  expect(readme).toContain("npm run generate:api");
});
