package com.example.parceltracker.legacysettings

import android.content.Context
import expo.modules.kotlin.exception.Exceptions
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition

/**
 * Reads what the .NET MAUI version of the app stored. That version kept its settings in a SharedPreferences file
 * named "parceltracker" (the name it had inherited from the Xamarin version). JavaScript storage libraries use
 * their own files and cannot see it.
 */
class LegacySettingsModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("LegacySettings")

    AsyncFunction("getString") { key: String ->
      val context = appContext.reactContext ?: throw Exceptions.ReactContextLost()
      context.getSharedPreferences("parceltracker", Context.MODE_PRIVATE).getString(key, null)
    }
  }
}
