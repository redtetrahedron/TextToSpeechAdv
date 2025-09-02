//-----------------------------------------------------------------------
// <copyright file="CrossTextToSpeech.shared.cs" company="redtetrahedron">
//     Author: Samuel DiPiazza
//     Copyright (c) redtetrahedron. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace TextToSpeech.Maui.Plugin;

/// <summary>
/// Cross platform TTS implemenations
/// </summary>
public static class CrossTextToSpeech
{

    #region Fields

    private static Lazy<ITextToSpeech> implementation = new(
        CreateTextToSpeech,
        LazyThreadSafetyMode.PublicationOnly);

    #endregion

    #region Private methods

    private static ITextToSpeech CreateTextToSpeech()
    {
#if ANDROID
        return new Platforms.Android.TextToSpeech();
#else 
        return null;
#endif
    }

    #endregion

    #region Internal methods

    internal static Exception NotImplementedInReferenceAssembly()
                                  => new NotImplementedException(
                                      "This functionality is not implemented in the portable version of this assembly.  You should reference the NuGet package from your main application project in order to reference the platform-specific implementation.");

    #endregion

    #region Public properties

    /// <summary>
    /// Gets if the plugin is supported on the current platform.
    /// </summary>
    public static bool IsSupported => implementation.Value != null;

    /// <summary>
    /// Current plugin implementation to use
    /// </summary>
    public static ITextToSpeech Current => implementation.Value ?? throw NotImplementedInReferenceAssembly();

    #endregion

    #region Public methods

    /// <summary>
    /// Dispose of TTS, reset lazy load
    /// </summary>
    public static void Dispose()
    {
        if (implementation.Value != null
           && implementation.IsValueCreated)
        {
            implementation.Value.Dispose();
            implementation = new Lazy<ITextToSpeech>(CreateTextToSpeech, LazyThreadSafetyMode.PublicationOnly);
        }
    }

    #endregion

}
