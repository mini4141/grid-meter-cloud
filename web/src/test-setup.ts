import '@testing-library/jest-dom/vitest';

// jsdom lacks ResizeObserver, which Carbon components use.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};
