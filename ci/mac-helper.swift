// Small macOS helpers for ci/probe-macos.sh.
//   handler <scheme>               prints the app that opens <scheme>:// links
//   sethandler <scheme> <bundleid> makes <bundleid> the app that opens them
//   gurl <pid> <url>               sends the "open this URL" Apple Event to one process
import AppKit
import CoreServices

let a = CommandLine.arguments
switch a.count > 1 ? a[1] : "" {
case "handler":
    let url = NSWorkspace.shared.urlForApplication(toOpen: URL(string: a[2] + "://x")!)
    print(url?.path ?? "none")
case "sethandler":
    print(LSSetDefaultHandlerForURLScheme(a[2] as CFString, a[3] as CFString))
case "gurl":
    let target = NSAppleEventDescriptor(processIdentifier: pid_t(a[2])!)
    let ev = NSAppleEventDescriptor(eventClass: AEEventClass(kInternetEventClass), eventID: AEEventID(kAEGetURL),
                                    targetDescriptor: target, returnID: AEReturnID(kAutoGenerateReturnID),
                                    transactionID: AETransactionID(kAnyTransactionID))
    ev.setParam(NSAppleEventDescriptor(string: a[3]), forKeyword: keyDirectObject)
    print(AESendMessage(ev.aeDesc, nil, AESendMode(kAENoReply), kAEDefaultTimeout))
default:
    print("usage: handler|sethandler|gurl")
    exit(2)
}
