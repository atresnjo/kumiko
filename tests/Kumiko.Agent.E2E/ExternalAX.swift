import ApplicationServices
import Foundation

// Read only the PID supplied by the owned hidden probe. Never prompt, activate,
// enumerate other applications, synthesize input, or query the system-wide tree.
let pid = pid_t(CommandLine.arguments[1])!
let app = AXUIElementCreateApplication(pid)
AXUIElementSetMessagingTimeout(app, 2)
var nodes: [[String: Any]] = []

func attribute(_ element: AXUIElement, _ name: String) -> CFTypeRef? {
    var value: CFTypeRef?
    let result = AXUIElementCopyAttributeValue(element, name as CFString, &value)
    return result == .success ? value : nil
}

func visit(_ element: AXUIElement, _ depth: Int) {
    guard depth < 50, nodes.count < 5000 else { return }
    let role = attribute(element, kAXRoleAttribute) as? String ?? ""
    var actions: CFArray?
    let actionResult = AXUIElementCopyActionNames(element, &actions)
    nodes.append([
        "role": role,
        "title": attribute(element, kAXTitleAttribute) as? String ?? "",
        "description": attribute(element, kAXDescriptionAttribute) as? String ?? "",
        "identifier": attribute(element, kAXIdentifierAttribute) as? String ?? "",
        "actions": actions as? [String] ?? [],
        "actionsError": actionResult.rawValue,
        "depth": depth
    ])
    for child in attribute(element, kAXChildrenAttribute) as? [AXUIElement] ?? [] {
        visit(child, depth + 1)
    }
}

var windowsValue: CFTypeRef?
let windowsResult = AXUIElementCopyAttributeValue(app, kAXWindowsAttribute as CFString, &windowsValue)
let windows = windowsValue as? [AXUIElement] ?? []
visit(app, 0)
let result: [String: Any] = [
    "pid": pid,
    "trusted": AXIsProcessTrusted(),
    "windowsError": windowsResult.rawValue,
    "windowCount": windows.count,
    "nodes": nodes,
    "scope": "Only the supplied hidden application's PID; read-only AX client"
]
let json = try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys])
FileHandle.standardOutput.write(json)
