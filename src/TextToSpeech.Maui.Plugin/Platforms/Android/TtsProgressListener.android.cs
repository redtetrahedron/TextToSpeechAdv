//-----------------------------------------------------------------------
// <copyright file="TtsProgressListener.android.cs" company="redtetrahedron">
//     Author: Samuel DiPiazza
//     Copyright (c) redtetrahedron. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using Android.Speech.Tts;

namespace TextToSpeech.Maui.Plugin.Platforms.Android;

/// <summary>
/// Tts progress listener.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="T:Plugin.TextToSpeech.TtsProgressListener"/> class.
/// </remarks>
/// <param name="tcs">Tcs.</param>
public class TtsProgressListener(TaskCompletionSource<object> tcs)
    : UtteranceProgressListener
{

    #region Public methods

    /// <summary>
    /// Gets called on done to trigger next event.
    /// </summary>
    /// <param name="utteranceId">Utterance identifier.</param>
    public override void OnDone(string utteranceId) => tcs?.TrySetResult(null);

    /// <summary>
    /// Handles errors
    /// </summary>
    /// <param name="utteranceId">Utterance identifier.</param>
    public override void OnError(string utteranceId, TextToSpeechError textToSpeechError)
                             => tcs?.TrySetException(new ArgumentException($"Error with TTS engine on progress listener: {textToSpeechError}"));
    [Obsolete]
    public override void OnError(string utteranceId) => OnError(utteranceId, TextToSpeechError.Synthesis);

    /// <summary>
    /// Ons the start.
    /// </summary>
    /// <param name="utteranceId">Utterance identifier.</param>
    public override void OnStart(string utteranceId)
    {
    }

    #endregion

}