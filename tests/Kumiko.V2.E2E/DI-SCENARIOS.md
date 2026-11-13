# DI behavior scenarios

Before implementation, extend the storage application E2E flow to verify:

- Generated provider factories validate a missing IStore without constructing
  any service; scoped constructor dependencies resolve with scope validation on.
- A mapped debounced command keeps its scoped store alive through cancellation
  cleanup. Shutdown waits for an explicit cleanup gate before disposing it.
- A subscription retains a separate scoped store until its handle has stopped;
  asynchronous handle teardown finishes before service disposal.
- Replay reconstructs recorded state without constructing a scoped store.
- An opaque provider registration remains available at execution without an
  enumerated effect-service list. Generic Host registration also needs no list.

Retain services-di.txt with ordered lifecycle evidence alongside services PNG,
TXT and session artifacts. Existing storage behavior remains required.
