// The bridge Kumiko injects into every main-frame document of an embedded webview.
interface Window {
  kumiko?: { log(message: string): void };
}
