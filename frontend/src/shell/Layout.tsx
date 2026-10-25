import { Outlet } from "react-router";
import { Navigation } from "./Navigation";
import "./shell.css";

const MAIN_ID = "main-content";

export function Layout() {
  return (
    <div className="shell">
      <a className="skip-link" href={`#${MAIN_ID}`}>
        Skip to main content
      </a>
      <header className="top-bar">
        <span className="product-name">AI-Hub</span>
      </header>
      <div className="shell-body">
        <Navigation />
        <main id={MAIN_ID} className="main-region" tabIndex={-1}>
          <Outlet />
        </main>
      </div>
    </div>
  );
}
