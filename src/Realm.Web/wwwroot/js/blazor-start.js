// Starts Blazor by hand: the script tag in App.razor has autostart="false", so these reconnection options apply (03 section 3.9).
// UNVERIFIED API shape for .NET 10; the fallback is the numeric retryIntervalMilliseconds default of 3 s.
Blazor.start({
  circuit: {
    reconnectionOptions: {
      maxRetries: 60,
      retryIntervalMilliseconds: (attempt) => Math.min(1000 * 2 ** attempt, 10000),
    },
  },
});
