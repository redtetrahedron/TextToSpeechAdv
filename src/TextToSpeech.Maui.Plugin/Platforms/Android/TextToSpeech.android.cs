//-----------------------------------------------------------------------
// <copyright file="TextToSpeech.android.cs" company="redtetrahedron">
//     Author: Samuel DiPiazza
//     Copyright (c) redtetrahedron. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using Android.OS;
using Android.Speech.Tts;
using System.Text.RegularExpressions;
using AndroidApp = Android.App;
using AndroidTextToSpeech = Android.Speech.Tts.TextToSpeech;
using AndroidUtil = Android.Util;
using IOnInitListener = Android.Speech.Tts.TextToSpeech.IOnInitListener;
using Locale = Java.Util.Locale;

namespace TextToSpeech.Maui.Plugin.Platforms.Android;

/// <summary>
/// Text to speech implementation Android
/// </summary>
public partial class TextToSpeech
    : Java.Lang.Object,
      ITextToSpeech,
      IOnInitListener,
      IDisposable
{

    #region Constants

    private const int DefaultMaxSpeechLength = 4000;

    #endregion

    #region Fields

    private int count;
    private bool initialized;
    private TaskCompletionSource<bool> initTcs;
    private CrossLocale? language;
    private float pitch, speakRate;
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private string text;
    private AndroidTextToSpeech textToSpeech;
    private float? volume;

    private static readonly Dictionary<string, string> LatinToItalianMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "caelum", "cèlum" }, // /ˈtʃe.lum/
            { "domus", "dòmus" }, // /ˈdo.mus/
            { "regina", "re-JEE-na" }, // /reˈdʒi.na/
            { "ecce", "EH-chay" }, // /ˈɛt.tʃe/
            { "pater", "PAH-ter" } // /ˈpa.ter/
        };


    [GeneratedRegex(@"<phoneme alphabet=""ipa"" ph=""([^""]+)"">([^<]+)</phoneme>")]
    private static partial Regex PhonemeRegex();

    #endregion

    #region Explicit interface implementations

    void IDisposable.Dispose()
    {
        textToSpeech?.Stop();
        textToSpeech?.Dispose();
        textToSpeech = null;
        initialized = false;
    }

    #endregion

    #region Private methods

    /// <summary>
    /// In a different method as it can crash on older target/compile for some reason
    /// </summary>
    /// <returns></returns>
    private IEnumerable<CrossLocale> GetInstalledLanguagesLollipop()
    {
        var sdk = (int)Build.VERSION.SdkInt;
        if (sdk < 21)
        {
            return [];
        }

#if __ANDROID_21__
        return textToSpeech.AvailableLanguages
            .Select(a => new CrossLocale { Country = a.Country, Language = a.Language, DisplayName = a.DisplayName });
#endif
    }

    private Task<bool> Init()
    {
        if (initialized)
        {
            return Task.FromResult(true);
        }

        initTcs = new TaskCompletionSource<bool>();

        Console.WriteLine($"Current version: {(int)Build.VERSION.SdkInt}");
        AndroidUtil.Log.Info("CrossTTS", $"Current version: {(int)Build.VERSION.SdkInt}");
        textToSpeech = new(AndroidApp.Application.Context, this);

        return initTcs.Task;
    }

    private void SetDefaultLanguage() => SetDefaultLanguageNonLollipop();

    private void SetDefaultLanguageNonLollipop()
    {
        if (textToSpeech == null)
        {
            return;
        }

        //disable warning because we are checking ahead of time.
#pragma warning disable 0618
        var sdk = (int)Build.VERSION.SdkInt;
        if (sdk >= 18)
        {
            try
            {
#if __ANDROID_18__
                if (textToSpeech.DefaultLanguage == null
                   && textToSpeech.Language != null)
                {
                    textToSpeech.SetLanguage(textToSpeech.Language);
                }
                else if (textToSpeech.DefaultLanguage != null)
                {
                    textToSpeech.SetLanguage(textToSpeech.DefaultLanguage);
                }
#endif
            }
            catch
            {
                if (textToSpeech.Language != null)
                {
                    textToSpeech.SetLanguage(textToSpeech.Language);
                }
            }
        }
        else
        {
            if (textToSpeech.Language != null)
            {
                textToSpeech.SetLanguage(textToSpeech.Language);
            }
        }
#pragma warning restore 0618
    }

    private async Task Speak(CancellationToken cancelToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (language.HasValue
           && !string.IsNullOrWhiteSpace(language.Value.Language))
        {
            Locale locale = null;
            if (!string.IsNullOrWhiteSpace(language.Value.Country))
            {
                locale = new Locale(language.Value.Language, language.Value.Country);
            }
            else
            {
                locale = new Locale(language.Value.Language);
            }

            var result = textToSpeech.IsLanguageAvailable(locale);
            if (result == LanguageAvailableResult.CountryAvailable)
            {
                textToSpeech.SetLanguage(locale);
            }
            else
            {
                Console.WriteLine($"Locale: {locale} was not valid, setting to default.");
                SetDefaultLanguage();
            }
        }
        else
        {
            SetDefaultLanguage();
        }

        var tcs = new TaskCompletionSource<object>();

        textToSpeech.SetPitch(pitch);
        textToSpeech.SetSpeechRate(speakRate);

        textToSpeech.SetOnUtteranceProgressListener(new TtsProgressListener(tcs));
#pragma warning disable CS0618 // Type or member is obsolete

        count++;

        var map = new Dictionary<string, string> { [AndroidTextToSpeech.Engine.KeyParamUtteranceId] = count.ToString() };

        if (volume.HasValue)
        {
            map.Add(AndroidTextToSpeech.Engine.KeyParamVolume, volume.ToString());
        }
        textToSpeech.Speak(text, QueueMode.Flush, map);

#pragma warning restore CS0618 // Type or member is obsolete

        void OnCancel()
        {
            textToSpeech.Stop();
            tcs.TrySetCanceled();
        }

        using (cancelToken.Register(OnCancel))
        {
            await tcs.Task;
        }
    }

    private static (string text, bool isSsml) ParseSsml(string input)
    {
        if (!input.Contains("<speak"))
            return (input, false);

        // Basic SSML parsing for phoneme tags
        var regex = PhonemeRegex();
        string modifiedText = regex.Replace(input, m =>
        {
            string ipa = m.Groups[1].Value;
            string grapheme = m.Groups[2].Value;
            // Fallback to phonetic spelling if SSML phoneme fails
            return MapIpaToItalian(ipa, grapheme);
        });
        return (modifiedText, true);
    }

    private static string MapIpaToItalian(string ipa, string grapheme)
    {
        // Simplified IPA-to-Italian mapping (extend as needed)
        return ipa switch
        {
            "ˈtʃe.lum" => "cèlum",
            "ˈdo.mus" => "dòmus",
            _ => grapheme // Fallback to original text
        };
    }

    private static string MapLatinToItalian(string latinText)
    {
        string modifiedText = latinText;
        foreach (var pair in LatinToItalianMap)
        {
            modifiedText = modifiedText.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }
        return modifiedText;
    }

    #endregion

    #region Public properties

    /// <summary>
    /// Gets the max string length of the speech engine -1 means no limit
    /// </summary>
    public int MaxSpeechInputLength
                   => (int)Build.VERSION.SdkInt < 18
                       ? DefaultMaxSpeechLength
                       : AndroidTextToSpeech.MaxSpeechInputLength;

    #endregion

    #region Public methods

    /// <summary>
    /// Get all installed and valide lanaguages
    /// </summary>
    /// <returns>List of CrossLocales</returns>
    public async Task<IEnumerable<CrossLocale>> GetInstalledLanguages()
    {
        await Init();
        if (textToSpeech != null
           && initialized)
        {
            var version = (int)Build.VERSION.SdkInt;
            var isLollipop = version >= 21;
            if (isLollipop)
            {
                try
                {
                    //in a different method as it can crash on older target/compile for some reason
                    return GetInstalledLanguagesLollipop();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Something went horribly wrong, defaulting to old implementation to get languages: {ex}");
                }
            }

            var languages = new List<CrossLocale>();
            var allLocales = Locale.GetAvailableLocales();
            foreach (var locale in allLocales)
            {
                try
                {
                    var result = textToSpeech.IsLanguageAvailable(locale);

                    if (result == LanguageAvailableResult.CountryAvailable)
                    {
                        languages.Add(
                            new CrossLocale
                            {
                                Country = locale.Country,
                                Language = locale.Language,
                                DisplayName = locale.DisplayName
                            });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking language; {locale} {ex}");
                }
            }

            return languages.GroupBy(c => c.ToString()).Select(g => g.First());
        }
        else
        {
            return Locale.GetAvailableLocales()
                .Where(
                    a => !string.IsNullOrWhiteSpace(a.Language)
                         && !string.IsNullOrWhiteSpace(a.Country))
                .Select(
                    a => new CrossLocale { Country = a.Country, Language = a.Language, DisplayName = a.DisplayName })
                .GroupBy(c => c.ToString())
                .Select(g => g.First());
        }
    }

    #region IOnInitListener implementation

    /// <summary>
    /// OnInit of TTS
    /// </summary>
    /// <param name="status"></param>
    public void OnInit(OperationResult status)
    {
        if (status.Equals(OperationResult.Success))
        {
            initialized = true;
            initTcs.TrySetResult(true);
        }
        else
        {
            initTcs.TrySetException(new ArgumentException("Failed to initialize TTS engine"));
        }
    }

    #endregion

    /// <summary>
    /// Speak back text
    /// </summary>
    /// <param name="text">Text to speak</param>
    /// <param name="crossLocale">Locale of voice</param>
    /// <param name="pitch">Pitch of voice</param>
    /// <param name="speakRate">Speak Rate of voice (All) (0.0 - 2.0f)</param>
    /// <param name="volume">Volume of voice (0.0-1.0)</param>
    /// <param name="cancelToken">Canelation token to stop speak</param>
    /// <exception cref="ArgumentNullException">Thrown if text is null</exception>
    /// <exception cref="ArgumentException">Thrown if text length is greater than maximum allowed</exception>
    public async Task Speak(
                          string text,
                          CrossLocale? crossLocale = null,
                          float? pitch = null,
                          float? speakRate = null,
                          float? volume = null,
                          CancellationToken cancelToken = default)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text), "Text can not be null");
        }

        if (text.Length >= MaxSpeechInputLength)
        {
            throw new ArgumentException("Text length is over the maximum speech input length.", nameof(text));
        }

        try
        {
            await semaphore.WaitAsync(cancelToken);
            this.text = text;
            language = crossLocale;
            this.pitch = pitch ?? 1.0f;
            this.speakRate = speakRate ?? 1.0f;
            this.volume = volume;

            // TODO: need to wait lock so not to break people using queuing mechanism
            await Init();

            await Speak(cancelToken);
        }
        finally
        {
            if (semaphore.CurrentCount == 0)
            {
                semaphore.Release();
            }
        }
    }

    public async Task EcclesiasticalLatinSpeech(string text, float? pitch = null, float? speakRate = null, float? volume = null,
                          CancellationToken cancelToken = default)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var (modifiedText, isSsml) = ParseSsml(text);
        var locale = new CrossLocale() { Language = "it", Country = "IT" };

        // Apply Latin pronunciation mapping for Ecclesiastical Latin (non-SSML)
        string finalText = isSsml ? modifiedText : MapLatinToItalian(modifiedText);

        await Speak(finalText, locale, pitch, speakRate, volume, cancelToken);
    }


    #endregion

}
