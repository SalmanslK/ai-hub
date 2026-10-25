import { useCallback, useEffect, useState } from "react";
import type { ApiResult } from "./client";

// Runs an API call when the screen mounts; `retry` sends it again. Pass a stable `request`
// (defined outside the component, or memoised), or it runs on every render.
export function useApiRequest<T>(request: () => Promise<ApiResult<T>>) {
  const [result, setResult] = useState<ApiResult<T>>();

  useEffect(() => {
    let current = true;
    request().then((next) => {
      if (current) setResult(reported(next));
    });
    return () => {
      current = false;
    };
  }, [request]);

  const retry = useCallback(async () => {
    setResult(reported(await request()));
  }, [request]);

  return { data: result?.ok ? result.data : undefined, failed: result?.ok === false, retry };
}

// The correlation id is for support, never for the screen.
function reported<T>(result: ApiResult<T>) {
  if (!result.ok && result.correlationId) console.error(`Request failed; correlation id ${result.correlationId}`);
  return result;
}
