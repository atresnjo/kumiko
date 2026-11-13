// The bridge Kumiko injects into the page's main frame.
interface Window {
  kumiko?: {
    log(message: string): void;
    platform: "macos" | "windows" | "linux";
    window: {
      close(): void;
      minimize(): void;
      toggleMaximize(): void;
      setTitle(title: string): void;
    };
  };
}
