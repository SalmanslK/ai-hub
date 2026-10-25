import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, expect, it, vi } from "vitest";
import { routes } from "./App";

// A criterion's test carries its id in its name: it("… [AC R-001.AC-1]", …).
const AREAS = [
  { name: "Dashboard", path: "/" },
  { name: "Employee intelligence", path: "/intelligence" },
  { name: "Recommendations", path: "/recommendations" },
  { name: "Knowledge hub", path: "/knowledge" },
  { name: "Privacy and governance", path: "/governance" },
];

afterEach(() => {
  cleanup();
  window.history.pushState({}, "", "/");
});

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  return router;
}

// The landmark stays visible; on a narrow screen only its links collapse behind "Menu".
function mainNavigation() {
  return screen.getByRole("navigation", { name: "Main" });
}

function navigationLinks() {
  return within(mainNavigation()).getAllByRole("link", { hidden: true });
}

function pageHeading() {
  return within(screen.getByRole("main")).getByRole("heading", { level: 1 });
}

function expectBefore(first: Node, second: Node) {
  expect(first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
}

it("shows the skip link, top bar, main navigation and the Dashboard heading, in order [AC R-009.AC-1]", () => {
  renderAt("/");

  const main = screen.getByRole("main");
  const headings = within(main).getAllByRole("heading", { level: 1 });
  expect(headings.map((heading) => heading.textContent)).toEqual(["Dashboard"]);

  const skipLink = screen.getByRole("link", { name: "Skip to main content" });
  expect(skipLink.getAttribute("href")).toBe(`#${main.id}`);
  const banner = screen.getByRole("banner");
  expect(banner.textContent).toContain("AI-Hub");
  const navigation = mainNavigation();

  expectBefore(skipLink, banner);
  expectBefore(banner, navigation);
  expectBefore(navigation, main);
});

it.each(["/", "/knowledge"])(
  "lists the five areas in order and marks only the current one, at %s [AC R-009.AC-2]",
  (path) => {
    renderAt(path);

    const links = navigationLinks();
    expect(links.map((link) => [link.textContent, link.getAttribute("href")])).toEqual(
      AREAS.map((area) => [area.name, area.path]),
    );
    expect(links.map((link) => link.getAttribute("aria-current"))).toEqual(
      AREAS.map((area) => (area.path === path ? "page" : null)),
    );
  },
);

it("moves to Recommendations in the page when its link is activated [AC R-009.AC-3]", async () => {
  const router = renderAt("/");
  const link = navigationLinks().find((candidate) => candidate.textContent === "Recommendations");
  expect(link).toBeDefined();

  const notPrevented = fireEvent.click(link!);

  expect(notPrevented).toBe(false);
  expect(await screen.findByRole("heading", { level: 1, name: "Recommendations" })).toBeDefined();
  expect(router.state.location.pathname).toBe("/recommendations");
  expect(navigationLinks().filter((candidate) => candidate.getAttribute("aria-current") === "page")
    .map((candidate) => candidate.textContent)).toEqual(["Recommendations"]);
});

it.each(AREAS)("opens $path directly with the $name heading [AC R-009.AC-4]", ({ name, path }) => {
  renderAt(path);

  expect(pageHeading().textContent).toBe(name);
});

it("shows the previous screen again when the person goes back [AC R-009.AC-4]", async () => {
  const router = renderAt("/knowledge");
  fireEvent.click(navigationLinks().find((link) => link.textContent === "Recommendations")!);
  expect(await screen.findByRole("heading", { level: 1, name: "Recommendations" })).toBeDefined();

  await act(() => router.navigate(-1));

  expect(pageHeading().textContent).toBe("Knowledge hub");
  expect(router.state.location.pathname).toBe("/knowledge");
});

it("opens the address in the browser's location bar and goes back with the browser [AC R-009.AC-4]", async () => {
  window.history.pushState({}, "", "/knowledge");
  vi.resetModules();
  const { App } = await import("./App");
  render(<App />);
  expect(pageHeading().textContent).toBe("Knowledge hub");

  fireEvent.click(navigationLinks().find((link) => link.textContent === "Recommendations")!);
  expect(await screen.findByRole("heading", { level: 1, name: "Recommendations" })).toBeDefined();
  expect(window.location.pathname).toBe("/recommendations");

  act(() => window.history.back());

  expect(await screen.findByRole("heading", { level: 1, name: "Knowledge hub" })).toBeDefined();
  expect(window.location.pathname).toBe("/knowledge");
});

it("shows Page not found inside the shell for an unknown address [AC R-009.AC-5]", () => {
  renderAt("/no-such-page");

  expect(screen.getByRole("banner").textContent).toContain("AI-Hub");
  expect(navigationLinks()).toHaveLength(AREAS.length);
  expect(pageHeading().textContent).toBe("Page not found");
  expect(screen.getByRole("main").textContent).toContain("The page you asked for does not exist.");
  const home = within(screen.getByRole("main")).getByRole("link", { name: "Go to the dashboard" });
  expect(home.getAttribute("href")).toBe("/");
});

it("opens and closes the navigation links with the Menu button [AC R-009.AC-6]", () => {
  renderAt("/");
  const menu = screen.getByRole("button", { name: "Menu" });
  expect(menu.getAttribute("aria-expanded")).toBe("false");

  fireEvent.click(menu);

  expect(menu.getAttribute("aria-expanded")).toBe("true");
  expect(within(mainNavigation()).getAllByRole("link").map((link) => link.textContent))
    .toEqual(AREAS.map((area) => area.name));

  fireEvent.click(menu);

  expect(menu.getAttribute("aria-expanded")).toBe("false");
  expect(within(mainNavigation()).queryAllByRole("link")).toHaveLength(0);
});
