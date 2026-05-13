import Foundation

/// Read/write access to autofill-related preferences shared between the main app and
/// the iOS Autofill extension via the App Group UserDefaults suite.
///
/// All underlying identifiers (suite name + key names) come from `VaultConstants`
/// so they are defined exactly once across the project.
public enum AutofillSettings {
    private static var sharedDefaults: UserDefaults? {
        UserDefaults(suiteName: VaultConstants.userDefaultsSuite)
    }

    /// Whether the autofill extension should copy a credential's current TOTP code to
    /// the clipboard when the user selects it for autofill.
    /// Defaults to `true` when the key has never been written.
    public static var shouldCopyTotpOnFill: Bool {
        get {
            guard let defaults = sharedDefaults else { return true }
            if defaults.object(forKey: VaultConstants.autofillCopyTotpOnFillKey) == nil {
                return true
            }
            return defaults.bool(forKey: VaultConstants.autofillCopyTotpOnFillKey)
        }
        set {
            sharedDefaults?.set(newValue, forKey: VaultConstants.autofillCopyTotpOnFillKey)
        }
    }
}
