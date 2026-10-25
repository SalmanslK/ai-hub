import createClient from "openapi-fetch";
import type { paths } from "./schema";

// The only file that imports openapi-fetch. A call never throws: a problem-details answer, a network
// error or a body that is not JSON comes back as a failure.

type GetPath = { [P in keyof paths]: paths[P] extends { get: object } ? P : never }[keyof paths];

type OkBody<P extends GetPath> = paths[P]["get"] extends {
  responses: { 200: { content: { "application/json": infer Body } } };
}
  ? Body
  : never;

export type ApiFailure = { ok: false; status?: number; title?: string; correlationId?: string };
export type ApiResult<T> = { ok: true; data: T } | ApiFailure;

const PROBLEM_JSON = "application/problem+json";

// Looked up at call time, so a test's stubbed fetch is the one used. Node's Request needs an absolute URL.
const client = createClient<paths>({
  baseUrl: window.location.origin,
  fetch: (request) => globalThis.fetch(request),
});

export async function apiGet<P extends GetPath>(path: P): Promise<ApiResult<OkBody<P>>> {
  try {
    // A GET with no parameters; openapi-fetch's overloads cannot be resolved for a generic path.
    const { data, error, response } = await client.GET(path as never);
    if (response.ok) return { ok: true, data: data as OkBody<P> };
    return failureFrom(response, error);
  } catch {
    return { ok: false };
  }
}

function failureFrom(response: Response, body: unknown): ApiFailure {
  const isProblem = response.headers.get("Content-Type")?.includes(PROBLEM_JSON);
  if (!isProblem || typeof body !== "object" || body === null) return { ok: false, status: response.status };
  const { title, correlationId } = body as { title?: string; correlationId?: string };
  return { ok: false, status: response.status, title, correlationId };
}
