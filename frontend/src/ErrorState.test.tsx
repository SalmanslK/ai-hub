import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { apiGet } from "./api/client";
import { useApiRequest } from "./api/useApiRequest";
import { ErrorState } from "./shell/ErrorState";

// A criterion's test carries its id in its name: it("… [AC R-001.AC-1]", …).
// No screen loads data yet, so a test screen shows how one uses the request hook and the error state.
const PROBLEM = {
  type: "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  title: "An error occurred while processing your request.",
  status: 500,
  detail: "System.InvalidOperationException: boom at AiHub.Health.Check() in HealthCheck.cs:line 42",
  correlationId: "0HN7Q2J1K3L4M",
};
const HEALTHY = { status: "Healthy", checks: [] };

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

const loadHealth = () => apiGet("/health");

function HealthScreen() {
  const { data, failed, retry } = useApiRequest(loadHealth);
  if (failed) return <ErrorState onRetry={retry} />;
  return data ? <p>Status: {data.status}</p> : <p>Loading</p>;
}

function stubFetch(...responses: Array<() => Response>) {
  const fetchMock = vi.fn<typeof fetch>();
  for (const respond of responses) {
    fetchMock.mockImplementationOnce(async () => respond());
  }
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function problem() {
  return new Response(JSON.stringify(PROBLEM), {
    status: PROBLEM.status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

function healthy() {
  return new Response(JSON.stringify(HEALTHY), { status: 200, headers: { "Content-Type": "application/json" } });
}

function renderInMain() {
  render(
    <main>
      <HealthScreen />
    </main>,
  );
}

it("shows the error state in the main region and none of the problem's details [AC R-009.AC-9]", async () => {
  stubFetch(problem);
  renderInMain();

  const alert = await within(screen.getByRole("main")).findByRole("alert");

  expect(alert.textContent).toContain("Something went wrong");
  expect(alert.textContent).toContain("We could not load this page. Check your connection and try again.");
  expect(screen.getByRole("button", { name: "Try again" })).toBeDefined();
  const shown = document.body.textContent ?? "";
  expect(shown).not.toContain(PROBLEM.detail);
  expect(shown).not.toContain("HealthCheck.cs");
  expect(shown).not.toContain(String(PROBLEM.status));
  expect(shown).not.toContain(PROBLEM.correlationId);
  expect(shown).not.toContain(PROBLEM.title);
});

it("shows the error state when the network fails [AC R-009.AC-9]", async () => {
  const fetchMock = vi.fn<typeof fetch>().mockRejectedValue(new TypeError("Failed to fetch"));
  vi.stubGlobal("fetch", fetchMock);
  renderInMain();

  const alert = await screen.findByRole("alert");

  expect(alert.textContent).toContain("Something went wrong");
  expect(document.body.textContent).not.toContain("Failed to fetch");
});

it("sends the request again on Try again and shows the content when it succeeds [AC R-009.AC-10]", async () => {
  const fetchMock = stubFetch(problem, healthy);
  renderInMain();
  await screen.findByRole("alert");

  fireEvent.click(screen.getByRole("button", { name: "Try again" }));

  expect(await screen.findByText("Status: Healthy")).toBeDefined();
  expect(screen.queryByRole("alert")).toBeNull();
  expect(fetchMock).toHaveBeenCalledTimes(2);
});
