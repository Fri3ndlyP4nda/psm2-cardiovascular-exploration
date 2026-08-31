using System;
using System.IO;
using Cardio.Core;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Writes the game's sound cues as WAV files.
    ///
    /// The project commits no binary asset it cannot regenerate from source, so
    /// the audio is synthesised here rather than imported: a few sine tones with
    /// an envelope, one per <see cref="AudioCue"/>. They are placeholders and
    /// the report should say so - the point is that an event has audible
    /// feedback at the right moment, not that the feedback sounds good.
    ///
    /// WAV rather than a runtime-built AudioClip because a clip created with
    /// AudioClip.Create does not survive being saved as an asset: Unity stores
    /// the object but not its samples, so it would load back silent.
    /// </summary>
    public static class AudioFactory
    {
        private const int SampleRate = 44100;
        private static string Folder => "Assets/Resources/Audio";

        /// <summary>One tone in a cue: frequency in Hz, and when it starts and stops.</summary>
        private struct Tone
        {
            public float Frequency;
            public float Start;
            public float Duration;
            public float Gain;

            public Tone(float frequency, float start, float duration, float gain = 1f)
            {
                Frequency = frequency;
                Start = start;
                Duration = duration;
                Gain = gain;
            }
        }

        public static void CreateCues()
        {
            Directory.CreateDirectory(Folder);

            // Rising major third: the conventional "yes".
            Write(AudioCue.Correct, 0.34f, new[]
            {
                new Tone(660f, 0f, 0.14f),
                new Tone(880f, 0.12f, 0.20f)
            });

            // Low, flat and slightly detuned: reads as "no" without being harsh.
            Write(AudioCue.Wrong, 0.30f, new[]
            {
                new Tone(196f, 0f, 0.26f),
                new Tone(185f, 0f, 0.26f, 0.6f)
            });

            // Soft high chime for a hint becoming available.
            Write(AudioCue.Hint, 0.42f, new[]
            {
                new Tone(1046f, 0f, 0.18f, 0.5f),
                new Tone(1318f, 0.14f, 0.26f, 0.4f)
            });

            // Short low thud for taking damage.
            Write(AudioCue.Damage, 0.22f, new[]
            {
                new Tone(140f, 0f, 0.18f),
                new Tone(94f, 0.02f, 0.20f, 0.7f)
            });

            // Three-note arpeggio for finishing a level.
            Write(AudioCue.LevelComplete, 0.72f, new[]
            {
                new Tone(523f, 0f, 0.18f),
                new Tone(659f, 0.16f, 0.18f),
                new Tone(784f, 0.32f, 0.36f)
            });

            // Very short blip for a button.
            Write(AudioCue.Click, 0.09f, new[]
            {
                new Tone(880f, 0f, 0.07f, 0.45f)
            });

            AssetDatabase.Refresh();
            Debug.Log($"[PSM2] Audio cues written to {Folder}.");
        }

        private static void Write(AudioCue cue, float totalSeconds, Tone[] tones)
        {
            int totalSamples = Mathf.CeilToInt(SampleRate * totalSeconds);
            var samples = new float[totalSamples];

            foreach (Tone tone in tones)
            {
                int start = Mathf.Clamp(Mathf.RoundToInt(tone.Start * SampleRate), 0, totalSamples);
                int length = Mathf.Clamp(Mathf.RoundToInt(tone.Duration * SampleRate), 0, totalSamples - start);

                for (int i = 0; i < length; i++)
                {
                    float t = (float)i / SampleRate;

                    // Short attack, exponential decay. Without the attack every
                    // cue starts on a click, which is far more noticeable than
                    // the tone itself.
                    float progress = (float)i / length;
                    float attack = Mathf.Min(1f, progress / 0.06f);
                    float decay = Mathf.Exp(-4f * progress);

                    samples[start + i] += Mathf.Sin(2f * Mathf.PI * tone.Frequency * t) * attack * decay * tone.Gain;
                }
            }

            Normalise(samples, 0.85f);
            File.WriteAllBytes($"{Folder}/{cue}.wav", EncodeWav(samples, SampleRate));
        }

        /// <summary>Scales the loudest peak to <paramref name="peak"/> so no cue clips or is inaudible.</summary>
        private static void Normalise(float[] samples, float peak)
        {
            float max = 0f;
            foreach (float s in samples) max = Mathf.Max(max, Mathf.Abs(s));
            if (max <= 0.0001f) return;

            float scale = peak / max;
            for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
        }

        /// <summary>Standard 16-bit mono PCM WAV.</summary>
        private static byte[] EncodeWav(float[] samples, int sampleRate)
        {
            const int channels = 1;
            const int bitsPerSample = 16;

            int dataBytes = samples.Length * sizeof(short);
            var stream = new MemoryStream(44 + dataBytes);
            var w = new BinaryWriter(stream);

            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });

            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);                                            // PCM header size
            w.Write((short)1);                                      // PCM format
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * bitsPerSample / 8);      // byte rate
            w.Write((short)(channels * bitsPerSample / 8));          // block align
            w.Write((short)bitsPerSample);

            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);

            foreach (float sample in samples)
            {
                w.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }

            w.Flush();
            return stream.ToArray();
        }
    }
}
