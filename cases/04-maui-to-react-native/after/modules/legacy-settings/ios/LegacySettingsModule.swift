import ExpoModulesCore

/// Reads what the .NET MAUI version of the app stored. On iOS that version used the standard user defaults.
public class LegacySettingsModule: Module {
  public func definition() -> ModuleDefinition {
    Name("LegacySettings")

    AsyncFunction("getString") { (key: String) -> String? in
      UserDefaults.standard.string(forKey: key)
    }
  }
}
