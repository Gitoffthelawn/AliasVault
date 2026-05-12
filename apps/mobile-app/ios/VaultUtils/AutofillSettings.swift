import Foundation

/// Read-only access to autofill-related preferences shared between the main app and
/// the iOS Autofill extension via the App Group UserDefaults suite.
///
/// The setter side lives in NativeVaultManager (main app target) and writes to the
/// same suite. Keys and the suite identifier must stay in sync with VaultConstants.
public enum AutofillSettings {
    /// App Group identifier — must match `VaultConstants.userDefaultsSuite` and
    /// the autofill entitlements.
    private static let suiteName = "group.net.aliasvault.autofill"

    /// Key — must match `VaultConstants.autofillCopyTotpOnFillKey`.
    private static let copyTotpOnFillKey = "aliasvault_autofill_copy_totp_on_fill"

    private static var sharedDefaults: UserDefaults? {
        UserDefaults(suiteName: suiteName)
    }

    /// Whether the autofill extension should copy a credential's current TOTP code to
    /// the clipboard when the user selects it for autofill.
    /// Defaults to `true` when the key has never been written.
    public static var shouldCopyTotpOnFill: Bool {
        guard let defaults = sharedDefaults else { return true }
        if defaults.object(forKey: copyTotpOnFillKey) == nil {
            return true
        }
        return defaults.bool(forKey: copyTotpOnFillKey)
    }
}
