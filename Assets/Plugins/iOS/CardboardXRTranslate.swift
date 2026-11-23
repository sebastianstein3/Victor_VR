// On-device translation for the Workspace's live captions (LiveCaptions.cs), with Apple's Translation
// framework: TranslationSession(installedSource:target:) works without SwiftUI from iOS 26.4. The
// languages must be downloaded (Settings > Apps > Translate > Downloaded Languages); nothing leaves the
// phone. Earlier iOS versions get a message instead of a translation.
import Foundation
#if canImport(Translation)
import Translation
#endif

public typealias CXRTranslateCallback = @convention(c) (Int32, UnsafePointer<CChar>?, Int32) -> Void

@_cdecl("CardboardXR_Translate")
public func CardboardXR_Translate(_ requestId: Int32, _ text: UnsafePointer<CChar>?, _ source: UnsafePointer<CChar>?,
                                  _ target: UnsafePointer<CChar>?, _ callback: CXRTranslateCallback?) {
    guard let callback = callback, let text = text, let source = source, let target = target else { return }
    let input = String(cString: text)
    let from = String(cString: source)
    let to = String(cString: target)

    func reply(_ message: String, _ ok: Bool) {
        DispatchQueue.main.async {
            message.withCString { callback(requestId, $0, ok ? 1 : 0) }
        }
    }

    #if canImport(Translation)
    if #available(iOS 26.4, *) {
        Task {
            do {
                let session = try TranslationSession(installedSource: Locale.Language(identifier: from),
                                                     target: Locale.Language(identifier: to))
                let response = try await session.translate(input)
                reply(response.targetText, true)
            } catch {
                reply("Pobierz języki: Ustawienia > Aplikacje > Tłumacz (\(error.localizedDescription))", false)
            }
        }
        return
    }
    #endif
    reply("Tłumaczenie na żywo wymaga iOS 26.4 lub nowszego", false)
}
