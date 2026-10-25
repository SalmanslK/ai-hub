import { builtinEnvironments, type Environment } from "vitest/environments";

// Vitest's jsdom, keeping Node's AbortController and AbortSignal. The jsdom environment replaces them
// with jsdom's but keeps Node's Request, which rejects a jsdom signal — and react-router builds a Request
// with one on every navigation.
const jsdom = builtinEnvironments.jsdom;

export default {
  name: "jsdom-with-node-abort",
  transformMode: jsdom.transformMode,
  async setup(global, options) {
    const { AbortController, AbortSignal } = global;
    const environment = await jsdom.setup(global, options);
    Object.assign(global, { AbortController, AbortSignal });
    return environment;
  },
} satisfies Environment;
