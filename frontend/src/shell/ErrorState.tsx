import { useState } from "react";

type ErrorStateProps = { onRetry: () => void | Promise<void> };

export function ErrorState({ onRetry }: ErrorStateProps) {
  const [retrying, setRetrying] = useState(false);

  async function retry() {
    if (retrying) return;
    setRetrying(true);
    try {
      await onRetry();
    } finally {
      setRetrying(false);
    }
  }

  return (
    <div className="error-state">
      <div role="alert">
        <h2>Something went wrong</h2>
        <p>We could not load this page. Check your connection and try again.</p>
      </div>
      <button type="button" className="button-primary" onClick={retry}>
        {retrying ? "Trying again…" : "Try again"}
      </button>
    </div>
  );
}
