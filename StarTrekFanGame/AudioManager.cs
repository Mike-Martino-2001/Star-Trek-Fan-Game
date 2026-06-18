using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace StarTrekFanGame
{
    /// <summary>
    /// Owns all game audio:
    ///   * the Star Trek TNG theme, which plays for the whole session and
    ///     restarts 10 seconds after each play-through ends, and
    ///   * short sound effects (torpedo fire, explosion) played through small
    ///     voice pools so rapid, overlapping shots don't cut each other off.
    ///
    /// All MediaPlayer instances have thread affinity to the UI dispatcher, so
    /// this type must be constructed and used from the UI thread.
    /// </summary>
    sealed class AudioManager
    {
        // -- Background theme -------------------------------------------------
        private readonly MediaPlayer _music = new();
        private readonly DispatcherTimer _loopGap;

        // -- Sound-effect voice pools (round-robin to allow overlap) ----------
        private readonly MediaPlayer[] _torpedoVoices;
        private readonly MediaPlayer[] _explosionVoices;
        private int _torpedoIndex;
        private int _explosionIndex;

        private static Uri AudioUri(string fileName) =>
            new(Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", fileName));

        public AudioManager()
        {
            // Theme: open once, then restart 10 s after every natural end.
            _music.Open(AudioUri("StarTrekTheNextGeneration.mp3"));
            _music.Volume = 0.20;
            _loopGap = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _loopGap.Tick += (_, _) => { _loopGap.Stop(); PlayTheme(); };
            _music.MediaEnded += (_, _) => _loopGap.Start();

            _torpedoVoices   = BuildVoices("torpedo_fire.mp3", 6, 0.20);
            _explosionVoices = BuildVoices("explosion.mp3",    4, 0.20);
        }

        // Pre-open a set of players for one effect so playback is latency-free.
        private static MediaPlayer[] BuildVoices(string fileName, int count, double volume)
        {
            var uri = AudioUri(fileName);
            var voices = new MediaPlayer[count];
            for (int i = 0; i < count; i++)
            {
                var p = new MediaPlayer { Volume = volume };
                p.Open(uri);
                voices[i] = p;
            }
            return voices;
        }

        /// <summary>Begin (or resume) the looping theme. Call once at startup.</summary>
        public void StartTheme() => PlayTheme();

        private void PlayTheme()
        {
            _music.Position = TimeSpan.Zero;
            _music.Play();
        }

		/// <summary>Gets or sets the music volume (0.0 to 1.0).</summary>
		public double MusicVolume
		{
			get => _music.Volume;
			set => _music.Volume = value;
		}

		/// <summary>Gets or sets the SFX volume for all torpedo and explosion voices (0.0 to 1.0).</summary>
		public double SfxVolume
		{
			get => _torpedoVoices[0].Volume;
			set
			{
				foreach (var v in _torpedoVoices)   v.Volume = value;
				foreach (var v in _explosionVoices) v.Volume = value;
			}
		}

		public void PlayTorpedo()   => PlayOneShot(_torpedoVoices,   ref _torpedoIndex);
        public void PlayExplosion() => PlayOneShot(_explosionVoices, ref _explosionIndex);

        private static void PlayOneShot(MediaPlayer[] voices, ref int index)
        {
            var p = voices[index];
            index = (index + 1) % voices.Length;
            p.Position = TimeSpan.Zero;
            p.Play();
        }
    }
}
