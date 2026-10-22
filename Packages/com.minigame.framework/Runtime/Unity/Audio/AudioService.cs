using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MiniGame.Framework
{
    public readonly struct PlaybackHandle
    {
        internal readonly AudioService Owner;
        internal readonly int Index;
        internal readonly ulong Generation;
        internal PlaybackHandle(AudioService owner, int index, ulong generation) { Owner = owner; Index = index; Generation = generation; }
    }

    public sealed class AudioService : IDisposable
    {
        private sealed class Voice
        {
            public AudioSource Source;
            public ulong Generation;
            public int Priority;
            public double Started;
            public bool Active;
        }
        private readonly ResourceService resources;
        private readonly Dictionary<string, AssetLease<AudioClip>> clips = new Dictionary<string, AssetLease<AudioClip>>();
        private readonly HashSet<string> loading = new HashSet<string>();
        private readonly Voice[] voices;
        private readonly AudioSource music;
        private bool disposed;
        private bool unlocked;
        private bool paused;
        private bool musicActive;
        private bool muted;
        private float volume = 1;
        private readonly int clipCapacity;
        public bool IsUnlocked => unlocked;
        public bool IsPaused => paused;
        public bool IsMuted => muted;
        public int PreloadedCount => clips.Count;

        public AudioService(ResourceService resources, Transform root, int voiceCount = 8, int clipCapacity = 32)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            if (!root) throw new ArgumentNullException(nameof(root));
            if (voiceCount < 1 || voiceCount > 128) throw new ArgumentOutOfRangeException(nameof(voiceCount));
            if (clipCapacity < 1) throw new ArgumentOutOfRangeException(nameof(clipCapacity));
            this.clipCapacity = clipCapacity;
            voices = new Voice[voiceCount];
            for (int i = 0; i < voices.Length; i++) voices[i] = new Voice { Source = Create(root, "SFX " + i) };
            music = Create(root, "Music");
        }
        private static AudioSource Create(Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }
        public void Unlock() { Guard(); unlocked = true; }
        public async UniTask PreloadAsync(string key, CancellationToken cancellation = default)
        {
            Guard();
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Audio key required.", nameof(key));
            if (clips.ContainsKey(key)) return;
            if (loading.Contains(key)) throw new InvalidOperationException("Audio clip is already loading.");
            if (clips.Count + loading.Count >= clipCapacity) throw new InvalidOperationException("Audio preload capacity exhausted.");
            loading.Add(key);
            AssetLease<AudioClip> lease = null;
            try
            {
                lease = await resources.LoadAsync<AudioClip>(key, cancellation);
                Guard();
                clips.Add(key, lease);
                lease = null;
            }
            finally { lease?.Dispose(); loading.Remove(key); }
        }
        public bool TryPlaySfx(string key, int priority, out PlaybackHandle handle)
        {
            Guard();
            handle = default;
            if (!unlocked || paused || muted || !clips.TryGetValue(key, out var clip)) return false;
            Tick();
            int selected = -1;
            for (int i = 0; i < voices.Length; i++) if (!voices[i].Active) { selected = i; break; }
            if (selected < 0)
            {
                for (int i = 0; i < voices.Length; i++)
                    if (voices[i].Priority <= priority && (selected < 0 || voices[i].Priority < voices[selected].Priority ||
                        (voices[i].Priority == voices[selected].Priority && voices[i].Started < voices[selected].Started))) selected = i;
            }
            if (selected < 0) return false;
            Voice voice = voices[selected];
            voice.Source.Stop();
            voice.Source.clip = clip.Asset;
            voice.Source.volume = volume;
            voice.Priority = priority;
            voice.Started = Time.realtimeSinceStartupAsDouble;
            voice.Generation++;
            voice.Active = true;
            voice.Source.Play();
            handle = new PlaybackHandle(this, selected, voice.Generation);
            return true;
        }
        public bool PlayMusic(string key, bool loop = true)
        {
            Guard();
            if (!unlocked || paused || !clips.TryGetValue(key, out var clip)) return false;
            music.Stop();
            music.clip = clip.Asset;
            music.loop = loop;
            music.volume = muted ? 0 : volume;
            music.Play();
            musicActive = true;
            return true;
        }
        public bool Stop(PlaybackHandle handle)
        {
            if (disposed || !ReferenceEquals(handle.Owner, this) || handle.Index < 0 || handle.Index >= voices.Length) return false;
            Voice voice = voices[handle.Index];
            if (voice.Generation != handle.Generation || !voice.Active) return false;
            voice.Source.Stop(); voice.Source.clip = null; voice.Active = false;
            return true;
        }
        public void StopMusic() { if (disposed) return; music.Stop(); music.clip = null; musicActive = false; }
        public void SetPaused(bool value)
        {
            Guard();
            if (paused == value) return;
            paused = value;
            foreach (Voice voice in voices)
            {
                if (!voice.Active) continue;
                if (paused) voice.Source.Pause(); else voice.Source.UnPause();
            }
            if (musicActive) { if (paused) music.Pause(); else music.UnPause(); }
        }
        public void SetVolume(float value, bool mute)
        {
            Guard();
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            volume = Mathf.Clamp01(value); muted = mute;
            foreach (Voice voice in voices) voice.Source.volume = muted ? 0 : volume;
            music.volume = muted ? 0 : volume;
        }
        public void Tick()
        {
            if (disposed || paused) return;
            foreach (Voice voice in voices)
                if (voice.Active && !voice.Source.isPlaying) { voice.Active = false; voice.Source.clip = null; }
            if (musicActive && !music.isPlaying) musicActive = false;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Voice voice in voices)
                if (voice.Source) { voice.Source.Stop(); voice.Source.clip = null; UnityEngine.Object.Destroy(voice.Source.gameObject); }
            if (music) { music.Stop(); music.clip = null; UnityEngine.Object.Destroy(music.gameObject); }
            foreach (var lease in clips.Values) lease.Dispose();
            clips.Clear();
        }
        private void Guard() { if (disposed) throw new ObjectDisposedException(nameof(AudioService)); }
    }
}
