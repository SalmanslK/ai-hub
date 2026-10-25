import { Link } from "react-router";

export function NotFound() {
  return (
    <>
      <h1>Page not found</h1>
      <p>The page you asked for does not exist.</p>
      <Link to="/">Go to the dashboard</Link>
    </>
  );
}
