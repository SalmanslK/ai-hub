import { useState } from "react";
import { NavLink } from "react-router";

// The platform's main areas, in the order of the FSD's flow: the Dashboard (§6), then §3, §4, §5 and §7.
const AREAS = [
  { label: "Dashboard", path: "/" },
  { label: "Employee intelligence", path: "/intelligence" },
  { label: "Recommendations", path: "/recommendations" },
  { label: "Knowledge hub", path: "/knowledge" },
  { label: "Privacy and governance", path: "/governance" },
];

const LINKS_ID = "main-navigation-links";

// On a narrow screen the links collapse behind "Menu"; on a wide one shell.css shows them always.
export function Navigation() {
  const [open, setOpen] = useState(false);

  return (
    <nav className="navigation" aria-label="Main">
      <button
        type="button"
        className="menu-button"
        aria-expanded={open}
        aria-controls={LINKS_ID}
        onClick={() => setOpen(!open)}
      >
        Menu
      </button>
      <ul id={LINKS_ID} className="navigation-links" hidden={!open}>
        {AREAS.map((area) => (
          <li key={area.path}>
            <NavLink to={area.path} end={area.path === "/"} onClick={() => setOpen(false)}>
              {area.label}
            </NavLink>
          </li>
        ))}
      </ul>
    </nav>
  );
}
