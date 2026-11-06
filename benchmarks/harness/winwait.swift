// Launches an app, prints the milliseconds until its window is on screen, then closes it.
// Prints -1 if no window appears within 20 seconds.

import CoreGraphics
import Foundation

let process = Process()
process.executableURL = URL(fileURLWithPath: CommandLine.arguments[1])
process.standardOutput = FileHandle.nullDevice
process.standardError = FileHandle.nullDevice

let start = DispatchTime.now()
func elapsedNanoseconds() -> Double { Double(DispatchTime.now().uptimeNanoseconds - start.uptimeNanoseconds) }

try process.run()

var found = false
while !found && elapsedNanoseconds() < 20e9 {
    let windows = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as! [[String: Any]]
    found = windows.contains {
        ($0["kCGWindowOwnerPID"] as? Int32) == process.processIdentifier && ($0["kCGWindowLayer"] as? Int) == 0
    }
    if !found { usleep(2000) }
}

print(found ? elapsedNanoseconds() / 1e6 : -1)
usleep(400_000)
process.terminate()
process.waitUntilExit()
