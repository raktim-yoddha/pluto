using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Pluto;

/// <summary>
/// Pluto's sounds, played by Windows' own media player: soft struck-glass tones made on the fly (no sound files to
/// ship), or a file the user chose. Each note is a few partials that ring and fade at their own pace, with a breath of
/// room, so it sounds like an object rather than a beep.
/// </summary>
internal static class Chime
{
    private const int Rate = 48000;

    private static readonly Dictionary<SoundKind, byte[]> Own = new()
    {
        // A soft tap: one low, short note.
        [SoundKind.Started] = Render(0.45, 0.42, (523.25, 0.0, 1.0, 0.09)),

        // Done: a rising fourth, the second note ringing longer.
        [SoundKind.Finished] = Render(1.25, 0.8, (783.99, 0.0, 0.8, 0.32), (1046.5, 0.11, 1.0, 0.55)),

        // Needs you: a gentle two-note knock, falling, so it reads as a question.
        [SoundKind.NeedsYou] = Render(1.0, 0.85, (1318.5, 0.0, 0.9, 0.26), (987.77, 0.14, 1.0, 0.4)),
    };

    private static readonly MediaPlayer Player = new();

    /// <summary>
    /// Plays <paramref name="kind"/>'s sound: Pluto's own when <paramref name="file"/> is null, nothing when it is empty,
    /// otherwise that file, at <paramref name="volume"/> (0…100).
    /// </summary>
    public static async void Play(SoundKind kind, string? file, int volume)
    {
        try
        {
            if (file is { Length: 0 })
            {
                return;
            }

            Player.Volume = Math.Clamp(volume, 0, 100) / 100.0;
            if (file is not null && File.Exists(file))
            {
                Player.Source = MediaSource.CreateFromUri(new Uri(file));
            }
            else
            {
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(Own[kind].AsBuffer());
                stream.Seek(0);
                Player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            }

            Player.Play();
        }
        catch (Exception e)
        {
            Log.Write("Chime", e); // no audio device, or a file Windows can't play: stay quiet
        }
    }

    /// <summary>
    /// Mixes notes (Hz, start s, loudness, ring s) into a 16-bit mono WAV of <paramref name="seconds"/>, peaking at
    /// <paramref name="level"/> of full scale.
    /// </summary>
    private static byte[] Render(double seconds, double level, params (double Hz, double At, double Gain, double Ring)[] notes)
    {
        var count = (int)(Rate * seconds);
        var mix = new double[count];

        // Partials of a small glass bell: slightly stretched overtones that die away faster than the note itself.
        (double Ratio, double Gain, double Ring)[] partials = [(1, 1, 1), (2.01, 0.3, 0.55), (3.02, 0.11, 0.33), (4.17, 0.05, 0.2)];
        foreach (var (hz, at, gain, ring) in notes)
        {
            var start = (int)(at * Rate);
            for (var i = start; i < count; i++)
            {
                var t = (i - start) / (double)Rate;
                var attack = t < 0.004 ? 0.5 - (0.5 * Math.Cos(Math.PI * t / 0.004)) : 1; // a soft strike, no click
                var sample = 0.0;
                foreach (var (ratio, g, r) in partials)
                {
                    sample += g * Math.Exp(-t / (ring * r)) * Math.Sin(2 * Math.PI * hz * ratio * t);
                }

                mix[i] += gain * attack * sample;
            }
        }

        // A breath of room: two quiet echoes, close enough to read as space rather than repeats.
        foreach (var (delay, gain) in new[] { (0.031, 0.16), (0.047, 0.11) })
        {
            var d = (int)(delay * Rate);
            for (var i = count - 1; i >= d; i--)
            {
                mix[i] += mix[i - d] * gain;
            }
        }

        var peak = mix.Max(Math.Abs);
        var fade = (int)(0.03 * Rate);
        using var memory = new MemoryStream();
        using var w = new BinaryWriter(memory);
        w.Write("RIFF"u8);
        w.Write(36 + (count * 2));
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            var tail = Math.Min(1, (count - i) / (double)fade);
            w.Write((short)(mix[i] / peak * level * tail * short.MaxValue));
        }

        w.Flush();
        return memory.ToArray();
    }
}
