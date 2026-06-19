using StarTrekFanGame.Model;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace StarTrekFanGame
{
    public partial class MainWindow : Window
    {
        // -- Constants --------------------------------------------------------
        private const double BarrelLength  = 50.0;   // pixels (gun tip / bullet spawn)
        private const double GunMoveSpeed  = 3.5;    // pixels per tick (WASD keys)
        private const double BulletSpeed   = 7.5;    // pixels per tick
        private const double GunBaseOffset = 34.0;   // ship centre, px above canvas bottom
        private const double ShipSize      = 60.0;   // rendered ship sprite size (px)
        private const double TorpedoSize   = 22.0;   // rendered torpedo sprite height (px)

        // -- Random -----------------------------------------------------------
        private readonly Random _rng = new Random();

        // -- Game model -------------------------------------------------------
        private readonly GameModel Model = new GameModel();

        // -- Audio ------------------------------------------------------------
        private readonly AudioManager _audio = new();

        // -- Player health (displayed as "shield" pips) -----------------------
        private const int MaxHealth = 5;
        private int  _health   = MaxHealth;
        private int  _invuln   = 0;       // frames of post-hit invulnerability
        private bool _gameOver = false;

        // -- Levels -----------------------------------------------------------
        private int _level = 1;
        private bool _levelStarted = false;     // a level has shapes to clear

        // Level-transition phases: ship glides to base, then hyperspace screen shows.
        private enum WarpPhase { None, SlideToBase, Hyperspace }
        private WarpPhase _warpPhase = WarpPhase.None;
        private int       _warpTimer = 0;
        private const int WarpSlideFrames      = 60;   // ticks to glide ship (~2 s at 30 fps)
        private const int WarpHyperspaceFrames = 90;   // ticks of hyperspace screen (~3 s)
        private static readonly BitmapImage  HyperspaceImage  = LoadImage("Assets/Backgrounds/hyperspace.png");
        private static readonly ImageBrush   HyperspaceBrush  = MakeHyperspaceBrush();
        private static ImageBrush MakeHyperspaceBrush()
        {
            var b = new ImageBrush(HyperspaceImage) { Stretch = Stretch.UniformToFill };
            b.Freeze();
            return b;
        }

        private static readonly string[] BackgroundFiles =
        {
            "Assets/Backgrounds/background_1.png",
            "Assets/Backgrounds/background_2.png",
            "Assets/Backgrounds/background_3.png",
            "Assets/Backgrounds/background_4.png",
            "Assets/Backgrounds/background_5.png",
            "Assets/Backgrounds/background_6.png",
            "Assets/Backgrounds/background_7.png",
            "Assets/Backgrounds/background_8.png",
            "Assets/Backgrounds/background_9.png",
            "Assets/Backgrounds/background_10.png",
            "Assets/Backgrounds/background_11.png",
            "Assets/Backgrounds/background_12.png",
            "Assets/Backgrounds/background_13.png",
            "Assets/Backgrounds/background_14.png",
            "Assets/Backgrounds/background_15.png",
            "Assets/Backgrounds/background_16.png",
            "Assets/Backgrounds/background_17.png",
            "Assets/Backgrounds/background_18.png",
            "Assets/Backgrounds/background_19.png",
            "Assets/Backgrounds/background_20.png",
            "Assets/Backgrounds/background_21.png",
            "Assets/Backgrounds/background_22.png",
            "Assets/Backgrounds/background_23.png",
            "Assets/Backgrounds/background_24.png",
            "Assets/Backgrounds/background_25.png",
        };
        private readonly ImageBrush?[] _backgroundCache = new ImageBrush?[BackgroundFiles.Length];

        // -- Game loop (vsync-aligned, fixed timestep) ------------------------
        //  Driven by CompositionTarget.Rendering instead of a DispatcherTimer:
        //  the callback is part of WPF's render pass, so it stays aligned with
        //  the monitor refresh and never starves keyboard/mouse input the way a
        //  Render-priority timer did. A fixed-timestep accumulator advances the
        //  simulation at exactly LogicTicksPerSecond regardless of how fast the
        //  display refreshes (so the game runs at the same speed on a 60 Hz or
        //  144 Hz screen), and is clamped so a hitch can't trigger a spiral.
        //
        //  This is set to ~30 Hz to match the game's original feel: the old
        //  DispatcherTimer asked for 16 ms but, at Windows' default ~15.6 ms
        //  timer granularity, actually fired at roughly half that rate. All the
        //  movement/cooldown constants were tuned against that effective rate,
        //  so the simulation runs here at the same speed. Raise this single
        //  value to speed the whole game up, lower it to slow it down.
        private const double LogicTicksPerSecond = 30.0;
        private const double FixedStepMs         = 1000.0 / LogicTicksPerSecond;
        private const int    MaxStepsPerFrame    = 5;
        private bool     _running        = false;
        private bool     _loopPrimed     = false;   // first frame seeds the clock
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private double   _stepAccumMs    = 0.0;

        // -- Keyboard state ---------------------------------------------------
        private bool _aKey    = false;   // move ship left
        private bool _dKey    = false;   // move ship right
        private bool _wKey    = false;   // move ship up
        private bool _sKey    = false;   // move ship down

        // Fire is held from two independent sources (keyboard Space and the
        // left mouse button); the gun fires while either is down. Tracking them
        // separately stops one source's release from clearing the other's hold.
        private bool _fireKeyHeld   = false;
        private bool _fireMouseHeld = false;
        private bool FireHeld => _fireKeyHeld || _fireMouseHeld;

		// -- Pause state ------------------------------------------------------
		private bool _paused     = false;
		private bool _wasRunning = false;   // timer state captured on pause open

        // -- Muzzle-flash counter (frames remaining) ---------------------------
        private int _muzzleFlash = 0;

        // -- Phaser damage tick: toggles 0/1 each frame; damage only applied on
        //    even ticks, halving DPS relative to 1 HP every tick. ---------------
        private int _phaserDamageTick = 0;

        // -- Retained visuals -------------------------------------------------
        //  Each game object owns one WPF element that is created once and then
        //  only repositioned each frame. This avoids rebuilding the whole visual
        //  tree (and the GC pressure that caused the periodic stutter).
        private sealed class BulletVisual { public Line Trail = null!; public FrameworkElement Sprite = null!; }

        private readonly Dictionary<GameShape, FrameworkElement> _shapeVisuals      = new();
        private readonly Dictionary<ExplosionParticle, Ellipse>  _particleVisuals   = new();
        private readonly Dictionary<Bullet, BulletVisual>        _bulletVisuals     = new();
        private readonly Dictionary<EnemyBullet, Ellipse>        _enemyBulletVisuals = new();
        private readonly Dictionary<GameShape, Ellipse>          _shieldVisuals     = new();
        private readonly Dictionary<Powerup,    Image>            _powerupVisuals    = new();

        // Object pools – WPF elements are created once and recycled to avoid
        // ShapeCanvas.Children.Add/Remove calls (which invalidate the visual tree)
        // and to prevent GC pressure from per-bullet / per-particle allocations.
        private readonly Stack<Ellipse> _enemyBulletPool = new();
        private readonly Stack<Ellipse> _particlePool    = new();

        // Pre-allocated list reused by SyncShieldOverlays each frame.
        private readonly List<GameShape> _toRemoveShields = new();

        // Shared, frozen sprite sources (loaded once).
        // Eight compass-direction ship sprites - one per WASD movement direction.
        private static readonly BitmapImage ShipN  = LoadImage("Assets/spaceship/spaceship/rotations/north.png");
        private static readonly BitmapImage ShipNE = LoadImage("Assets/spaceship/spaceship/rotations/north-east.png");
        private static readonly BitmapImage ShipE  = LoadImage("Assets/spaceship/spaceship/rotations/east.png");
        private static readonly BitmapImage ShipSE = LoadImage("Assets/spaceship/spaceship/rotations/south-east.png");
        private static readonly BitmapImage ShipS  = LoadImage("Assets/spaceship/spaceship/rotations/south.png");
        private static readonly BitmapImage ShipSW = LoadImage("Assets/spaceship/spaceship/rotations/south-west.png");
        private static readonly BitmapImage ShipW  = LoadImage("Assets/spaceship/spaceship/rotations/west.png");
        private static readonly BitmapImage ShipNW = LoadImage("Assets/spaceship/spaceship/rotations/north-west.png");
        private static readonly BitmapImage TorpedoSprite = LoadImage("Assets/Ammo/torpedo.png");

        // Warp fuel cell pickup animation frames (9-frame Pulse cycle).
        private static readonly BitmapImage[] FuelCellFrames = LoadFuelCellFrames();
        private static BitmapImage[] LoadFuelCellFrames()
        {
            var frames = new BitmapImage[9];
            for (int i = 0; i < 9; i++)
                frames[i] = LoadImage($"Assets/Warp_fuel_cell/Warp_fuel_cell/animations/Pulse/unknown/frame_{i:D3}.png");
            return frames;
        }

        private const double PowerupDropChance = 0.45; // 45% drop rate from Spawners

        // Borg sprites: each 2D shape renders as its 3D Borg counterpart.
        //   Circle -> sphere,  Rectangle -> cube,  Triangle -> pyramid.
        // Every type ships several design variants; the one shown is chosen by the
        // enemy's size (smallest -> variant 0, largest -> the final variant).
        private static readonly BitmapImage[] SphereSprites  = LoadVariants("Borg_sphere",  4);
        private static readonly BitmapImage[] CubeSprites    = LoadVariants("Borg_cube",    4);
        private static readonly BitmapImage[] PyramidSprites = LoadVariants("Borg_pyramid", 3);

        // Spawn size range (see SpawnShapes); used to map a shape's size to a variant.
        private const double SpawnSizeMin = 18.0;
        private const double SpawnSizeMax = 50.0;

        // Red damage flash: how long a single hit flashes for (frames).
        private const int HitFlashFrames = 9;

        // -- Enemy AI constants -----------------------------------------------
        // Size-tier thresholds (SizeTier with 4 tiers, range 18-50):
        //   tier 0 (~18-26): ShieldGenerator   tier 1 (~26-34): ShieldGenerator
        //   tier 2 (~34-42): Fighter           tier 3 (~42-50): Spawner
        private const int SpawnerTier      = 3;   // largest tier
        private const int FighterTier      = 2;   // medium tier
        // tiers 0 and 1 → ShieldGenerator

        private const int SpawnerCooldown  = 300; // ticks between spawns (~10 s)
        private const int FighterCooldown  = 90;  // ticks between bursts (~3 s)
        private const int ShieldRadius     = 80;  // px — how close a generator must be to shield an ally
        private const double EnemyBulletSpeed  = 3.5;
        private const int   FighterSpread   = 3;   // number of projectiles per burst
        private const int   MaxEnemyBullets = 10;  // hard cap – keeps frame rate stable
        private const int   MaxParticles    = 50;  // cap total live particles across all explosions
        private const int   ParticlePoolSize = 66; // 3 simultaneous explosions pre-created

        // Free-running frame counter, drives the continuous low-health flash.
        private int _frame;

        // Last values pushed to the HUD text blocks. The HUD updates every frame
        // but these change rarely, so we skip the string interpolation (and its
        // per-frame allocations) unless the underlying value actually moved.
        private int  _shownScore  = int.MinValue;
        private int  _shownShapes = int.MinValue;
        private int  _shownLevel  = int.MinValue;

        // Persistent "chrome" visuals (player ship + HUD), created once in the ctor.
        private BitmapImage _lastMoveSprite = ShipN;
        private readonly Image _ship = new() { Width = ShipSize, Height = ShipSize, Source = ShipN };
        private readonly Ellipse   _gunFlash  = new() { Visibility = Visibility.Collapsed };
        private readonly TextBlock _hudScore  = new() { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Yellow };
        private readonly TextBlock _hudShapes = new() { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.LightGray };
        private readonly TextBlock _hudShields = new() { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Aqua };
        private readonly Polygon[] _shieldPips = new Polygon[MaxHealth];
        private readonly TextBlock _hudLevel  = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Aqua };
        private readonly TextBlock _hudClear  = new() { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.GreenYellow, Visibility = Visibility.Collapsed };

        // Photon-torpedo heat gauge (top-left, always visible).
        private const double HeatBarW = 150.0;
        private readonly Rectangle _heatBg   = new() { Width = HeatBarW, Height = 9, Stroke = Brushes.Gray, StrokeThickness = 1, Fill = Frozen(90, 0, 0, 0) };
        private readonly Rectangle _heatFill = new() { Height = 9 };
        private readonly TextBlock _heatText = new() { FontSize = 10, FontWeight = FontWeights.Bold };

        // Phaser beam visuals (outer glow + bright core + impact sparks; all retained on canvas).
        private static readonly SolidColorBrush PhaserBrush     = Frozen(255, 255, 80, 20);
        private static readonly SolidColorBrush PhaserCoreBrush = Frozen(255, 255, 240, 180);  // warm white core
        private readonly Line    _phaserBeamL = new() { StrokeThickness = 3.5 };
        private readonly Line    _phaserBeamR = new() { StrokeThickness = 3.5 };
        private readonly Line    _phaserCoreL = new() { StrokeThickness = 1.5 };
        private readonly Line    _phaserCoreR = new() { StrokeThickness = 1.5 };
        private readonly Ellipse _phaserImpL  = new() { Width = 20, Height = 20, RenderTransformOrigin = new Point(0.5, 0.5) };
        private readonly Ellipse _phaserImpR  = new() { Width = 20, Height = 20, RenderTransformOrigin = new Point(0.5, 0.5) };

        // Phaser tuning constants.
        private const double PhaserConvergeDist = 220.0;  // px ahead of ship centre where beams meet
        private const double PhaserSpread       = 16.0;   // perpendicular offset at the emitter
        private const int    PhaserDamageEvery  = 6;      // ticks between phaser damage applications
        private int _phaserDamageCooldown = 0;

        // Z-order layers (Canvas draws by ZIndex, so insertion order no longer matters).
        private const int ZShape = 0, ZPowerup = 1, ZParticle = 2, ZBullet = 3, ZGun = 4, ZHud = 5, ZReticle = 6;

        // -- Aiming reticle ---------------------------------------------------
        //  Replaces the hard-to-see system "Cross" cursor with a bright, drawn
        //  crosshair that scales with the Viewbox and sits above everything.
        //  Each stroke is drawn twice (dark backing + bright top) so it stays
        //  readable over both dark and bright backgrounds.
        private static readonly SolidColorBrush ReticleColor   = Frozen(255,  60, 255,  90);  // neon green
        private static readonly SolidColorBrush ReticleOutline = Frozen(210,   0,   0,   0);
        private readonly Canvas _reticle = new() { IsHitTestVisible = false };

        // -- Cached frozen brushes (allocated once; safe to share across threads) --
        private static readonly SolidColorBrush ParticleFill  = Frozen(255, 255, 160,  0);
        private static readonly SolidColorBrush DamageOverlay = Frozen(255, 255,  40, 40);
        private static readonly SolidColorBrush FlashFill    = Frozen(255, 255, 255, 160);
        private static readonly SolidColorBrush HintFill     = Frozen(130, 200, 200, 200);

        private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        // --------------------------------------------------------------------
        public MainWindow()
        {
            InitializeComponent();
            BuildChrome();
            BuildReticle();
            InitPools();
        }

        // Create the persistent player-ship + HUD visuals exactly once.
        private void BuildChrome()
        {
            _gunFlash.Fill = FlashFill;

            // Keep the pixel-art ship crisp.
            RenderOptions.SetBitmapScalingMode(_ship, BitmapScalingMode.NearestNeighbor);

            AddChrome(_ship,      ZGun);
            AddChrome(_gunFlash,  ZGun);
            AddChrome(_hudScore,  ZHud);
            AddChrome(_hudShapes, ZHud);
            AddChrome(_hudShields, ZHud);
            AddChrome(_hudLevel,  ZHud);
            AddChrome(_heatBg,    ZHud);
            AddChrome(_heatFill,  ZHud);   // drawn over the background bar
            AddChrome(_heatText,  ZHud);
            AddChrome(_hudClear,  ZHud);

            // Phaser beams: outer glow, inner core, and terminus impact sparks.
            _phaserBeamL.Stroke = _phaserBeamR.Stroke = PhaserBrush;
            _phaserBeamL.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 40, 0), ShadowDepth = 0, BlurRadius = 18, Opacity = 1.0 };
            _phaserBeamR.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 40, 0), ShadowDepth = 0, BlurRadius = 18, Opacity = 1.0 };
            _phaserBeamL.Visibility = _phaserBeamR.Visibility = Visibility.Collapsed;
            AddChrome(_phaserBeamL, ZBullet);
            AddChrome(_phaserBeamR, ZBullet);

            _phaserCoreL.Stroke = _phaserCoreR.Stroke = PhaserCoreBrush;
            _phaserCoreL.Visibility = _phaserCoreR.Visibility = Visibility.Collapsed;
            AddChrome(_phaserCoreL, ZBullet);
            AddChrome(_phaserCoreR, ZBullet);

            _phaserImpL.Fill   = _phaserImpR.Fill   = PhaserBrush;
            _phaserImpL.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 120, 0), ShadowDepth = 0, BlurRadius = 22, Opacity = 1.0 };
            _phaserImpR.Effect = new DropShadowEffect { Color = Color.FromRgb(255, 120, 0), ShadowDepth = 0, BlurRadius = 22, Opacity = 1.0 };
            _phaserImpL.RenderTransform = new ScaleTransform(1.0, 1.0);
            _phaserImpR.RenderTransform = new ScaleTransform(1.0, 1.0);
            _phaserImpL.Visibility = _phaserImpR.Visibility = Visibility.Collapsed;
            AddChrome(_phaserImpL, ZBullet);
            AddChrome(_phaserImpR, ZBullet);

            // Fixed-position HUD lines
            Canvas.SetLeft(_hudScore, 12);  Canvas.SetTop(_hudScore, 10);
            Canvas.SetLeft(_hudShapes, 12); Canvas.SetTop(_hudShapes, 33);

            // Shield pips (diamond icons) after the "SHIELDS" label.
            _hudShields.Text = "SHIELDS";
            Canvas.SetLeft(_hudShields, 12); Canvas.SetTop(_hudShields, 53);
            for (int i = 0; i < _shieldPips.Length; i++)
            {
                var pip = MakePip();
                _shieldPips[i] = pip;
                AddChrome(pip, ZHud);
                var tt = (TranslateTransform)pip.RenderTransform;
                tt.X = 80 + i * 20;
                tt.Y = 60;
            }
        }

        // A small diamond pip centred on the origin (positioned via translate).
        private static Polygon MakePip()
        {
            const double s = 7.0;
            return new Polygon
            {
                Points = new PointCollection
                {
                    new Point(0, -s), new Point(s, 0), new Point(0, s), new Point(-s, 0)
                },
                Stroke = Brushes.Aqua,
                StrokeThickness = 1.2,
                RenderTransform = new TranslateTransform()
            };
        }

        // Build the crosshair once: a backing (dark, thick) pass under a bright,
        // thinner pass, so the reticle reads clearly on any background.
        private void BuildReticle()
        {
            AddReticleStrokes(ReticleOutline, 3.0);
            AddReticleStrokes(ReticleColor,   1.5);

            // Centre dot (bright, on top).
            var dot = new Ellipse { Width = 2, Height = 2, Fill = ReticleColor };
            Canvas.SetLeft(dot, -1.0);
            Canvas.SetTop(dot,  -1.0);
            _reticle.Children.Add(dot);

            _reticle.Visibility = Visibility.Collapsed;   // shown once the cursor enters
            AddChrome(_reticle, ZReticle);

            // Park it at the canvas centre until the first mouse move.
            Canvas.SetLeft(_reticle, 480);
            Canvas.SetTop(_reticle,  270);
        }

        // One crosshair pass: four gapped arms plus a ring, all drawn relative to
        // the reticle's origin (the container Canvas is moved to the cursor).
        private void AddReticleStrokes(Brush stroke, double thickness)
        {
            const double gap = 3.0, arm = 8.0, ring = 6.5;
            _reticle.Children.Add(MakeReticleLine(0, -arm, 0, -gap, stroke, thickness));
            _reticle.Children.Add(MakeReticleLine(0,  gap, 0,  arm, stroke, thickness));
            _reticle.Children.Add(MakeReticleLine(-arm, 0, -gap, 0, stroke, thickness));
            _reticle.Children.Add(MakeReticleLine( gap, 0,  arm, 0, stroke, thickness));

            var circle = new Ellipse
            {
                Width = ring * 2, Height = ring * 2,
                Stroke = stroke, StrokeThickness = thickness
            };
            Canvas.SetLeft(circle, -ring);
            Canvas.SetTop(circle,  -ring);
            _reticle.Children.Add(circle);
        }

        private static Line MakeReticleLine(double x1, double y1, double x2, double y2,
                                            Brush stroke, double thickness) => new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap   = PenLineCap.Round
        };

        // Pre-populate the enemy-bullet and particle pools so the game never has
        // to call ShapeCanvas.Children.Add during normal play (only at pool exhaustion).
        private void InitPools()
        {
            for (int i = 0; i < MaxEnemyBullets; i++)
            {
                var el = MakeEnemyBulletEllipse();
                el.Visibility = Visibility.Collapsed;
                Panel.SetZIndex(el, ZBullet);
                ShapeCanvas.Children.Add(el);
                _enemyBulletPool.Push(el);
            }

            for (int i = 0; i < ParticlePoolSize; i++)
            {
                var e = new Ellipse { Fill = ParticleFill, Visibility = Visibility.Collapsed };
                Panel.SetZIndex(e, ZParticle);
                ShapeCanvas.Children.Add(e);
                _particlePool.Push(e);
            }
        }

        // Shared factory so the pool initialiser and the fallback path create
        // identical Ellipses (same Effect settings, same fill).
        private static Ellipse MakeEnemyBulletEllipse() => new Ellipse
        {
            Width  = 10,
            Height = 10,
            Fill   = EnemyBulletBrush,
            Effect = new DropShadowEffect
            {
                Color       = Color.FromRgb(0, 255, 80),
                ShadowDepth = 0,
                BlurRadius  = 14,
                Opacity     = 1.0
            }
        };

        // -- Image / background helpers ---------------------------------------
        private static BitmapImage LoadImage(string relativePath)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri($"pack://application:,,,/{relativePath}");
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // Load every design variant for a Borg shape. The first lives in
        // "Borg_x/Borg_x/...", the rest in sibling "Borg_x (n)/Borg_x/..." folders
        // (spaces escaped for the pack URI).
        private static BitmapImage[] LoadVariants(string baseName, int count)
        {
            var sprites = new BitmapImage[count];
            for (int i = 0; i < count; i++)
            {
                string folder = i == 0 ? baseName : $"{baseName}%20({i})";
                sprites[i] = LoadImage($"Assets/Borg/{folder}/{baseName}/rotations/unknown.png");
            }
            return sprites;
        }

        private ImageBrush GetBackground(int index)
        {
            return _backgroundCache[index] ??= new ImageBrush(LoadImage(BackgroundFiles[index]))
            {
                Stretch = Stretch.UniformToFill
            };
        }

        private void AddChrome(UIElement el, int z)
        {
            Panel.SetZIndex(el, z);
            ShapeCanvas.Children.Add(el);
        }

        // -- Canvas events ----------------------------------------------------
        private void ShapeCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            _audio.StartTheme();     // plays for the whole session, looping with a 10 s gap
            NewGame();
            Render();
        }

        // --------------------------------------------------------------------
        //  GAME / LEVELS
        // --------------------------------------------------------------------
        private void NewGame()
        {
            ClearField();
            _gameOver = false;
            _invuln   = 0;
            _health   = MaxHealth;
            Model.Score = 0;
            _ship.Opacity = 1.0;

            // Reset the ship to bottom-centre, pointing straight up.
            Model.Gun.GunX  = ShapeCanvas.ActualWidth / 2;
            Model.Gun.GunY  = ShapeCanvas.ActualHeight - GunBaseOffset;
            Model.Gun.Angle = 0;
            Model.Gun.ResetHeat();

            StartLevel(1);
        }

        private void StartLevel(int level)
        {
            _level           = level;
            _levelStarted    = true;
            _warpPhase       = WarpPhase.None;
            _warpTimer       = 0;
            _ship.Visibility = Visibility.Visible;
            _lastMoveSprite  = ShipN;
            _ship.Source     = ShipN;
            ShapeCanvas.Background = GetBackground((level - 1) % BackgroundFiles.Length);
            SpawnShapes(4 + level * 2);   // more targets each level
        }

        // Remove every dynamic entity (shapes / bullets / particles) and its visual.
        private void ClearField()
        {
            Model.Shapes.Clear();
            Model.Bullets.Clear();
            Model.Particles.Clear();
            foreach (var eb in Model.EnemyBullets) RemoveEnemyBulletVisual(eb);
            Model.EnemyBullets.Clear();

            foreach (var v in _shapeVisuals.Values)     ShapeCanvas.Children.Remove(v);
            // Return pooled visuals to their pools (hide) instead of removing from canvas.
            foreach (var e2 in _particleVisuals.Values) { e2.Visibility = Visibility.Collapsed; _particlePool.Push(e2); }
            foreach (var bv in _bulletVisuals.Values)
            {
                ShapeCanvas.Children.Remove(bv.Trail);
                ShapeCanvas.Children.Remove(bv.Sprite);
            }
            _shapeVisuals.Clear();
            _particleVisuals.Clear();
            _bulletVisuals.Clear();
            // _enemyBulletVisuals already returned to pool by RemoveEnemyBulletVisual above.
            _enemyBulletVisuals.Clear();
            foreach (var el in _shieldVisuals.Values)      ShapeCanvas.Children.Remove(el);
            _shieldVisuals.Clear();
            foreach (var el in _powerupVisuals.Values)     ShapeCanvas.Children.Remove(el);
            _powerupVisuals.Clear();
            Model.Powerups.Clear();
            _muzzleFlash = 0;
        }

        private void GameOver()
        {
            _gameOver = true;
            StopLoop();
        }

        // --------------------------------------------------------------------
        //  GAME LOOP DRIVER  (CompositionTarget.Rendering + fixed timestep)
        // --------------------------------------------------------------------
        private void StartLoop()
        {
            if (_running) return;
            _running        = true;
            _loopPrimed     = false;   // re-seed the clock so a long pause isn't replayed
            _stepAccumMs    = 0.0;
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopLoop()
        {
            if (!_running) return;
            _running = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_running) return;

            TimeSpan now = ((RenderingEventArgs)e).RenderingTime;

            // First frame after Start: seed the clock, draw once, advance nothing.
            if (!_loopPrimed)
            {
                _loopPrimed     = true;
                _lastRenderTime = now;
                Render();
                return;
            }

            double elapsedMs = (now - _lastRenderTime).TotalMilliseconds;
            if (elapsedMs <= 0) return;   // Rendering can fire twice with one timestamp
            _lastRenderTime = now;

            _stepAccumMs += elapsedMs;

            int steps = 0;
            while (_stepAccumMs >= FixedStepMs && steps < MaxStepsPerFrame)
            {
                Step();
                _stepAccumMs -= FixedStepMs;
                steps++;
                if (_gameOver) break;     // GameOver() stops the loop mid-batch
            }

            // After a hitch we'd be many steps behind; drop the debt rather than
            // sprinting to catch up (which would look like a fast-forward).
            if (steps >= MaxStepsPerFrame) _stepAccumMs = 0.0;

            // Always draw the frame we just simulated, including the final frame
            // when GameOver() stopped the loop mid-batch (so the banner shows).
            Render();
        }

        // One logic tick + redraw, for the manual "Step" button while stopped.
        private void StepOnce()
        {
            Step();
            Render();
        }

        private void ShapeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double cw = ShapeCanvas.ActualWidth;
            if (cw > 0)
                Model.Gun.GunX = Math.Clamp(Model.Gun.GunX, 40, cw - 40);
        }

        // --------------------------------------------------------------------
        //  GAME LOOP
        // --------------------------------------------------------------------
        private void Step()
        {
            double cw = ShapeCanvas.ActualWidth;
            double ch = ShapeCanvas.ActualHeight;
            if (cw < 1 || ch < 1) return;
            if (_gameOver) return;

            _frame++;   // drives the continuous low-health damage flash

            // -- 1. Move ship (WASD keys) — blocked during level transitions --
            if (_warpPhase == WarpPhase.None)
            {
                if (_aKey) Model.Gun.GunX -= GunMoveSpeed;
                if (_dKey) Model.Gun.GunX += GunMoveSpeed;
                if (_wKey) Model.Gun.GunY -= GunMoveSpeed;
                if (_sKey) Model.Gun.GunY += GunMoveSpeed;
            }
            else if (_warpPhase == WarpPhase.SlideToBase)
            {
                // Smoothly lerp toward bottom-centre each tick.
                Model.Gun.GunX += (cw / 2              - Model.Gun.GunX) * 0.12;
                Model.Gun.GunY += (ch - GunBaseOffset  - Model.Gun.GunY) * 0.12;
            }

            double minY = ShipSize / 2 + 6;
            double maxY = ch - GunBaseOffset;
            Model.Gun.GunX = Math.Clamp(Model.Gun.GunX, 40, cw - 40);
            Model.Gun.GunY = Math.Clamp(Model.Gun.GunY, minY, maxY);

            // -- 2. Gun tick (manages torpedo cooldowns / heat) ----------------
            Model.Gun.Tick();

            // -- 2b. Phaser beam damage — applied every PhaserDamageEvery ticks --
            bool phasersOn = _warpPhase == WarpPhase.None && FireHeld;
            if (phasersOn)
            {
                _audio.StartPhaser();
                if (_phaserDamageCooldown > 0) _phaserDamageCooldown--;
                if (_phaserDamageCooldown == 0)
                {
                    ApplyPhaserDamage();
                    _phaserDamageCooldown = PhaserDamageEvery;
                }
            }
            else
            {
                _audio.StopPhaser();
                _phaserDamageCooldown = 0;
            }

            // -- 3. Move shapes + wall bounce ---------------------------------
            foreach (var s in Model.Shapes)
            {
                s.X += s.VX;
                s.Y += s.VY;
                if (s.HitFlash > 0) s.HitFlash--;

                double r = s.CollisionRadius;
                if (s.X + r > cw) { s.X = cw - r; s.VX = -Math.Abs(s.VX); }
                if (s.X - r < 0)  { s.X = r;       s.VX =  Math.Abs(s.VX); }
                if (s.Y + r > ch) { s.Y = ch - r;  s.VY = -Math.Abs(s.VY); }
                if (s.Y - r < 0)  { s.Y = r;        s.VY =  Math.Abs(s.VY); }
            }

            // -- 3b. Enemy AI (spawners, fighters, shield generators) ----------
            if (_warpPhase == WarpPhase.None)
                UpdateEnemyAI(cw, ch);

            // -- 4. Shape�shape elastic collisions ----------------------------
            for (int i = 0; i < Model.Shapes.Count; i++)
            {
                for (int j = i + 1; j < Model.Shapes.Count; j++)
                {
                    var a  = Model.Shapes[i];
                    var b  = Model.Shapes[j];
                    double dx   = b.X - a.X;
                    double dy   = b.Y - a.Y;
                    double dist2 = dx * dx + dy * dy;
                    double minD  = a.CollisionRadius + b.CollisionRadius;

                    if (dist2 < minD * minD && dist2 > 1e-6)
                    {
                        double dist = Math.Sqrt(dist2);
                        double nx   = dx / dist;
                        double ny   = dy / dist;

                        // Separate overlapping shapes
                        double overlap = minD - dist;
                        a.X -= nx * overlap * 0.5;
                        a.Y -= ny * overlap * 0.5;
                        b.X += nx * overlap * 0.5;
                        b.Y += ny * overlap * 0.5;

                        // Elastic velocity exchange along the collision normal
                        double van = a.VX * nx + a.VY * ny;
                        double vbn = b.VX * nx + b.VY * ny;
                        if (van > vbn)          // only when approaching
                        {
                            double d = van - vbn;
                            a.VX -= d * nx;  a.VY -= d * ny;
                            b.VX += d * nx;  b.VY += d * ny;
                        }
                    }
                }
            }

            // -- 5. Move bullets + boundary -----------------------------------
            foreach (var b in Model.Bullets)
            {
                if (!b.IsActive) continue;
                b.X += b.VX;
                b.Y += b.VY;
                if (b.X < -10 || b.X > cw + 10 || b.Y < -10 || b.Y > ch + 10)
                    b.IsActive = false;
            }

            // -- 6. Bullet�shape hit detection --------------------------------
            for (int bi = Model.Bullets.Count - 1; bi >= 0; bi--)
            {
                var bullet = Model.Bullets[bi];
                if (!bullet.IsActive) continue;

                for (int si = Model.Shapes.Count - 1; si >= 0; si--)
                {
                    var shape = Model.Shapes[si];
                    double dx = bullet.X - shape.X;
                    double dy = bullet.Y - shape.Y;
                    double hitR = shape.CollisionRadius + 4;

                    if (dx * dx + dy * dy < hitR * hitR)
                    {
                        bullet.IsActive = false;
                        if (shape.IsShielded) break; // shield absorbs the hit
                        shape.Hits -= 2;  // photon torpedoes deal double damage

                        // Bigger enemies survive until their hits run out; each hit
                        // triggers a red flash (continuous once one hit from death).
                        if (shape.Hits > 0)
                        {
                            shape.HitFlash = HitFlashFrames;
                            break;
                        }

                        Model.Shapes.RemoveAt(si);
                        RemoveShapeVisual(shape);
                        CreateExplosion(shape.X, shape.Y);
                        TryDropFuelCell(shape);
                        Model.Score += 100;
                        break;
                    }
                }
            }

            // -- 6b. Shape vs player ship (take damage) -----------------------
            if (_invuln > 0)
            {
                _invuln--;
            }
            else
            {
                double px = Model.Gun.GunX;
                double py = Model.Gun.GunY;
                double pr = ShipSize * 0.35;          // forgiving hit radius

                for (int si = Model.Shapes.Count - 1; si >= 0; si--)
                {
                    var s  = Model.Shapes[si];
                    double dx = s.X - px;
                    double dy = s.Y - py;
                    double rr = pr + s.CollisionRadius;

                    if (dx * dx + dy * dy < rr * rr)
                    {
                        Model.Shapes.RemoveAt(si);
                        RemoveShapeVisual(s);
                        CreateExplosion(s.X, s.Y);     // crash blast + sound
                        _health--;
                        _invuln = 90;                  // ~1.5 s of i-frames
                        if (_health <= 0) GameOver();
                        break;                         // at most one hit per frame
                    }
                }
            }

            // -- 7. Age explosion particles -----------------------------------
            for (int i = Model.Particles.Count - 1; i >= 0; i--)
            {
                var p = Model.Particles[i];
                p.X  += p.VX;
                p.Y  += p.VY;
                p.VX *= 0.965;
                p.VY *= 0.965;
                p.Life--;
                if (p.Life <= 0)
                {
                    Model.Particles.RemoveAt(i);
                    RemoveParticleVisual(p);
                }
            }

            // -- 8. Cleanup ---------------------------------------------------
            for (int i = Model.Bullets.Count - 1; i >= 0; i--)
            {
                var b = Model.Bullets[i];
                if (!b.IsActive)
                {
                    RemoveBulletVisual(b);
                    Model.Bullets.RemoveAt(i);
                }
            }
            if (_muzzleFlash > 0) _muzzleFlash--;

            // -- 8c. Enemy bullet movement + player collision -----------------
            double epx = Model.Gun.GunX;
            double epy = Model.Gun.GunY;
            double epr = ShipSize * 0.35;
            for (int i = Model.EnemyBullets.Count - 1; i >= 0; i--)
            {
                var eb = Model.EnemyBullets[i];
                if (!eb.IsActive) { RemoveEnemyBulletVisual(eb); Model.EnemyBullets.RemoveAt(i); continue; }

                eb.X += eb.VX;
                eb.Y += eb.VY;

                // Out of bounds → deactivate.
                if (eb.X < -20 || eb.X > cw + 20 || eb.Y < -20 || eb.Y > ch + 20)
                {
                    eb.IsActive = false;
                    continue;
                }

                // Hit player → deal damage (respects invulnerability frames).
                if (_invuln <= 0)
                {
                    double ddx = eb.X - epx;
                    double ddy = eb.Y - epy;
                    if (ddx * ddx + ddy * ddy < (epr + 5) * (epr + 5))
                    {
                        eb.IsActive = false;
                        _health--;
                        _invuln = 60;
                        if (_health <= 0) GameOver();
                    }
                }
            }

            // -- 8d. Powerup movement + pickup ------------------------------------
            double ppx = Model.Gun.GunX;
            double ppy = Model.Gun.GunY;
            for (int i = Model.Powerups.Count - 1; i >= 0; i--)
            {
                var pu = Model.Powerups[i];

                // Drift downward.
                pu.Y += Powerup.DriftSpeed;

                // Advance animation frame.
                pu.FrameTick++;
                if (pu.FrameTick >= Powerup.FrameInterval)
                {
                    pu.FrameTick = 0;
                    pu.Frame = (pu.Frame + 1) % FuelCellFrames.Length;
                }

                // Remove if off-screen.
                if (pu.Y > ch + 40)
                {
                    pu.IsActive = false;
                    if (_powerupVisuals.TryGetValue(pu, out var remove))
                    {
                        ShapeCanvas.Children.Remove(remove);
                        _powerupVisuals.Remove(pu);
                    }
                    Model.Powerups.RemoveAt(i);
                    continue;
                }

                // Player pickup — fully restore shields.
                double pdx = pu.X - ppx;
                double pdy = pu.Y - ppy;
                double pickupR = Powerup.CollisionRadius + ShipSize * 0.35;
                if (pdx * pdx + pdy * pdy < pickupR * pickupR)
                {
                    _health = MaxHealth;
                    if (_powerupVisuals.TryGetValue(pu, out var collected))
                    {
                        ShapeCanvas.Children.Remove(collected);
                        _powerupVisuals.Remove(pu);
                    }
                    Model.Powerups.RemoveAt(i);
                }
            }

            // -- 8b. Level progression ----------------------------------------
            // Phase 1 (SlideToBase): ship glides to bottom-centre (movement driven
            // in section 1 above). Phase 2 (Hyperspace): background swaps to the
            // hyperspace image and the ship hides until StartLevel is called.
            if (_levelStarted && Model.Shapes.Count == 0 && _warpPhase == WarpPhase.None)
            {
                _warpPhase      = WarpPhase.SlideToBase;
                _warpTimer      = WarpSlideFrames;
                _lastMoveSprite = ShipS;   // ship faces downward during the glide-out
            }

            if (_warpPhase == WarpPhase.SlideToBase && --_warpTimer <= 0)
            {
                // Snap to exact position, then cut to hyperspace.
                Model.Gun.GunX   = cw / 2;
                Model.Gun.GunY   = ch - GunBaseOffset;
                _warpPhase       = WarpPhase.Hyperspace;
                _warpTimer       = WarpHyperspaceFrames;
                ShapeCanvas.Background = HyperspaceBrush;
                _ship.Visibility = Visibility.Collapsed;
            }
            else if (_warpPhase == WarpPhase.Hyperspace && --_warpTimer <= 0)
            {
                StartLevel(_level + 1);
            }
        }

        // --------------------------------------------------------------------
        //  FIRE & EXPLOSION
        // --------------------------------------------------------------------
        private void FireBullet()
        {
            double gx  = Model.Gun.GunX;
            double gy  = Model.Gun.GunY;
            double rad = Model.Gun.Angle * Math.PI / 180.0;

            Model.Bullets.Add(new Bullet
            {
                X  = gx + Math.Sin(rad) * BarrelLength,
                Y  = gy - Math.Cos(rad) * BarrelLength,
                VX = Math.Sin(rad) * BulletSpeed,
                VY = -Math.Cos(rad) * BulletSpeed,
                IsActive = true
            });

            _muzzleFlash = 3;
            Model.Gun.AddHeat();      // torpedo builds heat; phasers ignore it
            _audio.PlayTorpedo();
        }

        // Apply one tick of phaser damage to every enemy the beams intersect.
        private void ApplyPhaserDamage()
        {
            double rad   = Model.Gun.Angle * Math.PI / 180.0;
            double gx    = Model.Gun.GunX;
            double gy    = Model.Gun.GunY;
            double perpX = Math.Cos(rad);
            double perpY = Math.Sin(rad);
            double endX  = gx + Math.Sin(rad) * PhaserConvergeDist;
            double endY  = gy - Math.Cos(rad) * PhaserConvergeDist;

            double lx = gx - perpX * PhaserSpread;
            double ly = gy - perpY * PhaserSpread;
            double rx = gx + perpX * PhaserSpread;
            double ry = gy + perpY * PhaserSpread;

            for (int si = Model.Shapes.Count - 1; si >= 0; si--)
            {
                var    shape = Model.Shapes[si];
                double hitR  = shape.CollisionRadius + 4;

                if (!LineIntersectsCircle(lx, ly, endX, endY, shape.X, shape.Y, hitR) &&
                    !LineIntersectsCircle(rx, ry, endX, endY, shape.X, shape.Y, hitR))
                    continue;

                if (shape.IsShielded) continue;  // shield deflects phaser beams

                _phaserDamageTick ^= 1;
                if (_phaserDamageTick != 0)   // skip every other tick -> half DPS
                {
                    shape.HitFlash = HitFlashFrames;
                    continue;
                }
                shape.Hits--;
                if (shape.Hits > 0)
                {
                    shape.HitFlash = HitFlashFrames;
                    continue;
                }

                Model.Shapes.RemoveAt(si);
                RemoveShapeVisual(shape);
                CreateExplosion(shape.X, shape.Y);
                TryDropFuelCell(shape);
                Model.Score += 100;
            }
        }

        // Returns true if the line segment AB intersects the circle at C with radius r.
        private static bool LineIntersectsCircle(
            double ax, double ay, double bx, double by,
            double cx, double cy, double r)
        {
            double dx = bx - ax, dy = by - ay;
            double fx = ax - cx, fy = ay - cy;
            double a  = dx * dx + dy * dy;
            double b  = 2 * (fx * dx + fy * dy);
            double c  = fx * fx + fy * fy - r * r;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            double sq = Math.Sqrt(disc);
            double t1 = (-b - sq) / (2 * a);
            double t2 = (-b + sq) / (2 * a);
            return (t1 >= 0 && t1 <= 1) || (t2 >= 0 && t2 <= 1) || (t1 <= 0 && t2 >= 1);
        }

        private void CreateExplosion(double x, double y)
        {
            const int N = 22;
            int canAdd = Math.Min(N, MaxParticles - Model.Particles.Count);
            for (int i = 0; i < canAdd; i++)
            {
                double angle = i * (Math.PI * 2.0 / N) + _rng.NextDouble() * 0.35;
                double spd   = 1.25 + _rng.NextDouble() * 2.75;
                int    life  = 16 + _rng.Next(14);
                Model.Particles.Add(new ExplosionParticle
                {
                    X = x, Y = y,
                    VX = Math.Cos(angle) * spd,
                    VY = Math.Sin(angle) * spd,
                    Life    = life,
                    MaxLife = life
                });
            }

            _audio.PlayExplosion();
        }

        // --------------------------------------------------------------------
        //  ENEMY AI
        // --------------------------------------------------------------------
        private void UpdateEnemyAI(double cw, double ch)
        {
            // Pass 1: reset shield flags so they are recomputed fresh each tick.
            foreach (var s in Model.Shapes)
                s.IsShielded = false;

            // Pass 2: shield generators mark nearby allies as shielded.
            foreach (var gen in Model.Shapes)
            {
                if (gen.Role != EnemyRole.ShieldGenerator) continue;
                foreach (var ally in Model.Shapes)
                {
                    if (ally == gen) continue;
                    double dx = ally.X - gen.X;
                    double dy = ally.Y - gen.Y;
                    if (dx * dx + dy * dy <= ShieldRadius * ShieldRadius)
                        ally.IsShielded = true;
                }
            }

            // Pass 3: spawners and fighters act on their cooldowns.
            double px = Model.Gun.GunX;
            double py = Model.Gun.GunY;

            // Collect new shapes spawned this tick (avoid mutating the list mid-loop).
            List<GameShape>? newShapes = null;

            foreach (var s in Model.Shapes)
            {
                if (s.ActionCooldown > 0) { s.ActionCooldown--; continue; }

                switch (s.Role)
                {
                    case EnemyRole.Spawner:
                        // Spawn a single fighter near the spawner.
                        double spx = s.X + _rng.NextDouble() * 60 - 30;
                        double spy = s.Y + _rng.NextDouble() * 60 - 30;
                        spx = Math.Clamp(spx, 40, cw - 40);
                        spy = Math.Clamp(spy, 40, ch * 0.7);
                        double fSz  = SpawnSizeMin + (SpawnSizeMax - SpawnSizeMin) * 0.45; // medium
                        double fAng = _rng.NextDouble() * Math.PI * 2;
                        double fSpd = 1.2 + _rng.NextDouble() * 1.2;
                        int    fType = _rng.Next(3);
                        GameShape fighter = fType switch
                        {
                            0 => new Circle(spx, spy, fSz)                  { VX = Math.Cos(fAng) * fSpd, VY = Math.Sin(fAng) * fSpd, MaxHits = SizeTier(fSz, SphereSprites.Length) + 1 },
                            1 => new RectShape(spx, spy, fSz * 1.5, fSz)    { VX = Math.Cos(fAng) * fSpd, VY = Math.Sin(fAng) * fSpd, MaxHits = SizeTier(fSz, CubeSprites.Length)   + 1 },
                            _ => new TriangleShape(spx, spy, fSz)           { VX = Math.Cos(fAng) * fSpd, VY = Math.Sin(fAng) * fSpd, MaxHits = SizeTier(fSz, PyramidSprites.Length)+ 1 }
                        };
                        fighter.Hits          = fighter.MaxHits;
                        fighter.Role          = EnemyRole.Fighter;
                        fighter.ActionCooldown = _rng.Next(FighterCooldown, FighterCooldown * 2);
                        newShapes ??= new List<GameShape>();
                        newShapes.Add(fighter);
                        s.ActionCooldown = SpawnerCooldown;
                        break;

                    case EnemyRole.Fighter:
                        // Fire a spread of green energy bolts toward the player.
                        double toDx = px - s.X;
                        double toDy = py - s.Y;
                        double dist = Math.Sqrt(toDx * toDx + toDy * toDy);
                        if (dist > 0.1 && Model.EnemyBullets.Count < MaxEnemyBullets)
                        {
                            double baseAng  = Math.Atan2(toDy, toDx);
                            double spreadArc = 0.35; // radians total spread
                            for (int b = 0; b < FighterSpread; b++)
                            {
                                double offset = FighterSpread > 1
                                    ? -spreadArc / 2 + spreadArc * b / (FighterSpread - 1)
                                    : 0;
                                double a = baseAng + offset;
                                Model.EnemyBullets.Add(new EnemyBullet
                                {
                                    X  = s.X, Y  = s.Y,
                                    VX = Math.Cos(a) * EnemyBulletSpeed,
                                    VY = Math.Sin(a) * EnemyBulletSpeed
                                });
                            }
                        }
                        s.ActionCooldown = FighterCooldown + _rng.Next(30);
                        break;
                }
            }

            // Add any newly spawned shapes and create their visuals.
            if (newShapes != null)
                foreach (var ns in newShapes)
                    Model.Shapes.Add(ns);
        }

        // --------------------------------------------------------------------
        //  SPAWN
        // --------------------------------------------------------------------
        private void SpawnShapes(int count = 6)
        {
            double cw = ShapeCanvas.ActualWidth  > 1 ? ShapeCanvas.ActualWidth  : 800;
            double ch = ShapeCanvas.ActualHeight > 1 ? ShapeCanvas.ActualHeight : 500;

            for (int i = 0; i < count; i++)
            {
                int    type = _rng.Next(3);
                double x    = 70  + _rng.NextDouble() * (cw - 140);
                double y    = 40  + _rng.NextDouble() * (ch * 0.62);
                double sz   = SpawnSizeMin + _rng.NextDouble() * (SpawnSizeMax - SpawnSizeMin);
                double ang  = _rng.NextDouble() * Math.PI * 2;
                double spd  = 1.0 + _rng.NextDouble() * 1.75;
                double vx   = Math.Cos(ang) * spd;
                double vy   = Math.Sin(ang) * spd;

                // Hit points scale with the visual size tier: smallest = 1 hit,
                // +1 for each larger size (matching the chosen sprite variant).
                GameShape shape = type switch
                {
                    0 => new Circle(x, y, sz)              { VX = vx, VY = vy, MaxHits = SizeTier(sz, SphereSprites.Length)  + 1 },
                    1 => new RectShape(x, y, sz * 1.5, sz) { VX = vx, VY = vy, MaxHits = SizeTier(sz, CubeSprites.Length)    + 1 },
                    _ => new TriangleShape(x, y, sz)        { VX = vx, VY = vy, MaxHits = SizeTier(sz, PyramidSprites.Length) + 1 }
                };
                shape.Hits = shape.MaxHits;

                // Assign enemy role based on size tier (uses 4-tier scale).
                int tier = SizeTier(sz, 4);
                shape.Role = tier switch
                {
                    >= SpawnerTier => EnemyRole.Spawner,
                    FighterTier    => EnemyRole.Fighter,
                    _              => EnemyRole.ShieldGenerator
                };
                // Stagger initial cooldowns so enemies don't all act at once.
                shape.ActionCooldown = _rng.Next(60, 180);

                Model.Shapes.Add(shape);
            }
        }

        // --------------------------------------------------------------------
        //  RENDER  (retained mode: visuals are created once, then repositioned)
        // --------------------------------------------------------------------
        private void Render()
        {
            double cw = ShapeCanvas.ActualWidth;
            double ch = ShapeCanvas.ActualHeight;

            SyncShapes();
            SyncParticles();
            SyncBullets();
            SyncEnemyBullets();
            SyncShieldOverlays();
            SyncPowerups();
            SyncPhaserBeams();
            UpdateGun(cw, ch);
            UpdateHud(cw, ch);
        }

        // -- Shapes -----------------------------------------------------------
        private void SyncShapes()
        {
            foreach (var shape in Model.Shapes)
            {
                if (!_shapeVisuals.TryGetValue(shape, out var v))
                {
                    v = CreateShapeVisual(shape);
                    _shapeVisuals[shape] = v;
                    Panel.SetZIndex(v, ZShape);
                    ShapeCanvas.Children.Add(v);
                }

                switch (shape)
                {
                    case Circle c:
                        Canvas.SetLeft(v, c.X - c.Radius);
                        Canvas.SetTop(v,  c.Y - c.Radius);
                        break;
                    case RectShape r:
                        Canvas.SetLeft(v, r.X - r.Width  / 2);
                        Canvas.SetTop(v,  r.Y - r.Height / 2);
                        break;
                    case TriangleShape t:
                        Canvas.SetLeft(v, t.X - t.Size);
                        Canvas.SetTop(v,  t.Y - t.Size);
                        break;
                }

                // Red damage flash (overlay is child index 1 of the sprite grid).
                //  - one hit from death: flash continuously until destroyed
                //  - otherwise: a brief flash that fades out after each hit taken
                var overlay = (Rectangle)((Grid)v).Children[1];
                if (shape.MaxHits > 1 && shape.Hits == 1)
                    overlay.Opacity = (_frame / 8) % 2 == 0 ? 0.6 : 0.0;
                else if (shape.HitFlash > 0)
                    overlay.Opacity = 0.6 * shape.HitFlash / HitFlashFrames;
                else
                    overlay.Opacity = 0.0;
            }
        }

        // Each shape becomes its Borg counterpart: sphere / cube / pyramid.
        // The variant is selected by the enemy's size so bigger foes look distinct.
        private static FrameworkElement CreateShapeVisual(GameShape shape) => shape switch
        {
            Circle c        => MakeSprite(PickVariant(SphereSprites,  c.Radius), c.Radius * 2, c.Radius * 2),
            RectShape r     => MakeSprite(PickVariant(CubeSprites,    r.Height), r.Width,      r.Height),
            TriangleShape t => MakeSprite(PickVariant(PyramidSprites, t.Size),   t.Size * 2,   t.Size * 2),
            _               => MakeSprite(SphereSprites[0],                      20,           20)
        };

        // Map a shape's size onto a variant index across the spawn size range.
        private static BitmapImage PickVariant(BitmapImage[] variants, double size)
            => variants[SizeTier(size, variants.Length)];

        // Bucket a shape's size into [0, tierCount) across the spawn size range.
        // Tier 0 is the smallest enemy; each higher tier is a larger visual size.
        private static int SizeTier(double size, int tierCount)
        {
            double norm  = (size - SpawnSizeMin) / (SpawnSizeMax - SpawnSizeMin);
            int    index = (int)(norm * tierCount);
            return Math.Clamp(index, 0, tierCount - 1);
        }

        // A Borg sprite sized to the shape's bounding box (positioned via Canvas),
        // with a red damage overlay clipped to the sprite's silhouette on top.
        // Index 0 = sprite, index 1 = overlay (see SyncShapes for the flash logic).
        private static Grid MakeSprite(BitmapImage source, double width, double height)
        {
            var img = new Image { Source = source, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);

            var overlay = new Rectangle
            {
                Fill             = DamageOverlay,
                OpacityMask      = new ImageBrush(source) { Stretch = Stretch.Uniform },
                Opacity          = 0.0,
                IsHitTestVisible = false
            };

            var grid = new Grid { Width = width, Height = height };
            grid.Children.Add(img);
            grid.Children.Add(overlay);
            return grid;
        }

        private void RemoveShapeVisual(GameShape shape)
        {
            if (_shapeVisuals.Remove(shape, out var v))
                ShapeCanvas.Children.Remove(v);
        }

        // -- Explosion particles  (shrink & fade) -----------------------------
        private void SyncParticles()
        {
            foreach (var p in Model.Particles)
            {
                if (!_particleVisuals.TryGetValue(p, out var e))
                {
                    if (_particlePool.Count > 0)
                    {
                        e = _particlePool.Pop();
                    }
                    else
                    {
                        e = new Ellipse { Fill = ParticleFill };
                        Panel.SetZIndex(e, ZParticle);
                        ShapeCanvas.Children.Add(e);
                    }
                    e.Visibility = Visibility.Visible;
                    _particleVisuals[p] = e;
                }

                double t  = (double)p.Life / p.MaxLife;
                double sz = 10.0 * t;
                if (sz < 0.5)
                {
                    e.Visibility = Visibility.Collapsed;
                    continue;
                }

                e.Visibility = Visibility.Visible;
                e.Width  = sz;
                e.Height = sz;
                e.Opacity = t;
                Canvas.SetLeft(e, p.X - sz / 2);
                Canvas.SetTop(e,  p.Y - sz / 2);
            }
        }

        private void RemoveParticleVisual(ExplosionParticle p)
        {
            if (_particleVisuals.Remove(p, out var e))
            {
                e.Visibility = Visibility.Collapsed;
                _particlePool.Push(e);
            }
        }

        // -- Bullets  (torpedo sprite + yellow exhaust trail) -----------------
        private void SyncBullets()
        {
            double tw = TorpedoSize * (TorpedoSprite.PixelWidth / (double)TorpedoSprite.PixelHeight);

            foreach (var b in Model.Bullets)
            {
                if (!b.IsActive) continue;

                if (!_bulletVisuals.TryGetValue(b, out var bv))
                {
                    var sprite = new Rectangle
                    {
                        Width       = tw,
                        Height      = TorpedoSize,
                        Fill        = Brushes.OrangeRed,
                        OpacityMask = new ImageBrush(TorpedoSprite) { Stretch = Stretch.Fill },
                        Effect      = new DropShadowEffect
                        {
                            Color       = Color.FromRgb(255, 30, 0),
                            ShadowDepth = 0,
                            BlurRadius  = 14,
                            Opacity     = 1.0
                        },
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new RotateTransform(
                            Math.Atan2(b.VX, -b.VY) * 180.0 / Math.PI)
                    };

                    bv = new BulletVisual
                    {
                        Trail  = new Line { Stroke = Brushes.OrangeRed, StrokeThickness = 2.0, Opacity = 0.75 },
                        Sprite = sprite
                    };
                    _bulletVisuals[b] = bv;
                    Panel.SetZIndex(bv.Trail,  ZBullet);
                    Panel.SetZIndex(bv.Sprite, ZBullet);
                    ShapeCanvas.Children.Add(bv.Trail);
                    ShapeCanvas.Children.Add(bv.Sprite);
                }

                bv.Trail.X1 = b.X - b.VX * 1.8;
                bv.Trail.Y1 = b.Y - b.VY * 1.8;
                bv.Trail.X2 = b.X;
                bv.Trail.Y2 = b.Y;
                Canvas.SetLeft(bv.Sprite, b.X - tw / 2);
                Canvas.SetTop(bv.Sprite,  b.Y - TorpedoSize / 2);
            }
        }

        private void RemoveBulletVisual(Bullet b)
        {
            if (_bulletVisuals.Remove(b, out var bv))
            {
                ShapeCanvas.Children.Remove(bv.Trail);
                ShapeCanvas.Children.Remove(bv.Sprite);
            }
        }

        // -- Enemy bullets (green glowing ellipses) ----------------------------
        private static readonly SolidColorBrush EnemyBulletBrush = Frozen(255, 0, 230, 60);

        private void SyncEnemyBullets()
        {
            foreach (var eb in Model.EnemyBullets)
            {
                if (!eb.IsActive) continue;

                if (!_enemyBulletVisuals.TryGetValue(eb, out var el))
                {
                    if (_enemyBulletPool.Count > 0)
                    {
                        el = _enemyBulletPool.Pop();
                    }
                    else
                    {
                        // Pool exhausted (shouldn't happen normally): allocate and add.
                        el = MakeEnemyBulletEllipse();
                        Panel.SetZIndex(el, ZBullet);
                        ShapeCanvas.Children.Add(el);
                    }
                    el.Visibility = Visibility.Visible;
                    _enemyBulletVisuals[eb] = el;
                }

                Canvas.SetLeft(el, eb.X - 5);
                Canvas.SetTop(el,  eb.Y - 5);
            }
        }

        private void RemoveEnemyBulletVisual(EnemyBullet eb)
        {
            if (_enemyBulletVisuals.Remove(eb, out var el))
            {
                el.Visibility = Visibility.Collapsed;
                _enemyBulletPool.Push(el);
            }
        }

        // -- Shield overlays (green glow ring over shielded enemies) ----------
        private static readonly SolidColorBrush ShieldFill = Frozen(55, 0, 255, 100);
        private static readonly SolidColorBrush ShieldStroke = Frozen(200, 0, 255, 120);

        private void SyncShieldOverlays()
        {
            // Remove overlays for enemies that are no longer shielded or gone.
            // Reuse the pre-allocated list to avoid a heap allocation every frame.
            _toRemoveShields.Clear();
            foreach (var kv in _shieldVisuals)
            {
                if (!Model.Shapes.Contains(kv.Key) || !kv.Key.IsShielded)
                    _toRemoveShields.Add(kv.Key);
            }
            foreach (var key in _toRemoveShields)
            {
                ShapeCanvas.Children.Remove(_shieldVisuals[key]);
                _shieldVisuals.Remove(key);
            }

            // Add/update overlays for currently shielded enemies.
            foreach (var shape in Model.Shapes)
            {
                if (!shape.IsShielded) continue;

                double r  = shape.CollisionRadius + 8;
                double sz = r * 2;

                if (!_shieldVisuals.TryGetValue(shape, out var shield))
                {
                    shield = new Ellipse
                    {
                        Stroke          = ShieldStroke,
                        StrokeThickness = 3,
                        Fill            = ShieldFill,
                        Effect = new DropShadowEffect
                        {
                            Color       = Color.FromRgb(0, 220, 100),
                            ShadowDepth = 0,
                            BlurRadius  = 18,
                            Opacity     = 0.9
                        }
                    };
                    _shieldVisuals[shape] = shield;
                    Panel.SetZIndex(shield, ZShape + 1);
                    ShapeCanvas.Children.Add(shield);
                }

                // Pulse opacity slightly each frame using the frame counter.
                shield.Opacity  = 0.65 + 0.25 * Math.Sin(_frame * 0.18);
                shield.Width    = sz;
                shield.Height   = sz;
                Canvas.SetLeft(shield, shape.X - r);
                Canvas.SetTop(shield,  shape.Y - r);
            }
        }

        // Drop a warp fuel cell from a killed enemy if it was a Spawner-tier enemy.
        private void TryDropFuelCell(GameShape shape)
        {
            if (shape.Role != EnemyRole.Spawner) return;
            if (_rng.NextDouble() >= PowerupDropChance) return;

            var pu = new Powerup { X = shape.X, Y = shape.Y };
            Model.Powerups.Add(pu);
        }

        // Retained-visual sync for warp fuel cell pickups.
        private void SyncPowerups()
        {
            foreach (var pu in Model.Powerups)
            {
                if (!_powerupVisuals.TryGetValue(pu, out var img))
                {
                    img = new Image
                    {
                        Width  = 44,
                        Height = 44,
                        Source = FuelCellFrames[pu.Frame]
                    };
                    _powerupVisuals[pu] = img;
                    Panel.SetZIndex(img, ZPowerup);
                    ShapeCanvas.Children.Add(img);
                }
                else
                {
                    img.Source = FuelCellFrames[pu.Frame];
                }

                Canvas.SetLeft(img, pu.X - 22);
                Canvas.SetTop(img,  pu.Y - 22);
            }
        }

        private void SyncPhaserBeams()
        {
            bool phasersOn = _warpPhase == WarpPhase.None && FireHeld;
            if (!phasersOn)
            {
                _phaserBeamL.Visibility = _phaserBeamR.Visibility = Visibility.Collapsed;
                _phaserCoreL.Visibility = _phaserCoreR.Visibility = Visibility.Collapsed;
                _phaserImpL.Visibility  = _phaserImpR.Visibility  = Visibility.Collapsed;
                return;
            }

            double rad   = Model.Gun.Angle * Math.PI / 180.0;
            double gx    = Model.Gun.GunX;
            double gy    = Model.Gun.GunY;
            double perpX = Math.Cos(rad);
            double perpY = Math.Sin(rad);

            // Default convergence point (max beam reach).
            double baseEndX = gx + Math.Sin(rad) * PhaserConvergeDist;
            double baseEndY = gy - Math.Cos(rad) * PhaserConvergeDist;

            // Wing-emitter start positions.
            double lx = gx - perpX * PhaserSpread;
            double ly = gy - perpY * PhaserSpread;
            double rx = gx + perpX * PhaserSpread;
            double ry = gy + perpY * PhaserSpread;

            // Shorten each beam independently to stop at the nearest intersecting shape.
            double tL = BeamFirstHitT(lx, ly, baseEndX, baseEndY);
            double tR = BeamFirstHitT(rx, ry, baseEndX, baseEndY);

            double lEndX = lx + tL * (baseEndX - lx);
            double lEndY = ly + tL * (baseEndY - ly);
            double rEndX = rx + tR * (baseEndX - rx);
            double rEndY = ry + tR * (baseEndY - ry);

            // Per-frame animation: outer flickers fast with organic wobble, core offset in phase.
            double t          = _frame * 0.18;
            double outerPulse = 0.70 + 0.30 * Math.Sin(t * 7.0 + Math.Sin(t * 3.1) * 1.2);
            double corePulse  = 0.65 + 0.35 * Math.Sin(t * 7.0 + 1.1);
            double impScale   = 0.70 + 0.60 * Math.Abs(Math.Sin(t * 8.0));

            // Outer glow beams.
            _phaserBeamL.X1 = lx;    _phaserBeamL.Y1 = ly;
            _phaserBeamL.X2 = lEndX; _phaserBeamL.Y2 = lEndY;
            _phaserBeamR.X1 = rx;    _phaserBeamR.Y1 = ry;
            _phaserBeamR.X2 = rEndX; _phaserBeamR.Y2 = rEndY;
            _phaserBeamL.Opacity = _phaserBeamR.Opacity = outerPulse;
            _phaserBeamL.Visibility = _phaserBeamR.Visibility = Visibility.Visible;

            // Inner bright core lines.
            _phaserCoreL.X1 = lx;    _phaserCoreL.Y1 = ly;
            _phaserCoreL.X2 = lEndX; _phaserCoreL.Y2 = lEndY;
            _phaserCoreR.X1 = rx;    _phaserCoreR.Y1 = ry;
            _phaserCoreR.X2 = rEndX; _phaserCoreR.Y2 = rEndY;
            _phaserCoreL.Opacity = _phaserCoreR.Opacity = corePulse;
            _phaserCoreL.Visibility = _phaserCoreR.Visibility = Visibility.Visible;

            // Impact sparks at each beam's terminus.
            const double impSz = 20.0;
            Canvas.SetLeft(_phaserImpL, lEndX - impSz / 2);
            Canvas.SetTop(_phaserImpL,  lEndY - impSz / 2);
            Canvas.SetLeft(_phaserImpR, rEndX - impSz / 2);
            Canvas.SetTop(_phaserImpR,  rEndY - impSz / 2);
            ((ScaleTransform)_phaserImpL.RenderTransform).ScaleX = impScale;
            ((ScaleTransform)_phaserImpL.RenderTransform).ScaleY = impScale;
            ((ScaleTransform)_phaserImpR.RenderTransform).ScaleX = impScale;
            ((ScaleTransform)_phaserImpR.RenderTransform).ScaleY = impScale;
            _phaserImpL.Opacity = _phaserImpR.Opacity = outerPulse;
            _phaserImpL.Visibility = _phaserImpR.Visibility = Visibility.Visible;
        }

        // Returns t ∈ [0,1] for the closest shape the segment (ax,ay)→(bx,by) hits, or 1.0.
        private double BeamFirstHitT(double ax, double ay, double bx, double by)
        {
            double minT = 1.0;
            double dx   = bx - ax;
            double dy   = by - ay;
            foreach (var shape in Model.Shapes)
            {
                double hitR = shape.CollisionRadius + 4;
                double fx   = ax - shape.X;
                double fy   = ay - shape.Y;
                double a    = dx * dx + dy * dy;
                double b    = 2 * (fx * dx + fy * dy);
                double c    = fx * fx + fy * fy - hitR * hitR;
                double disc = b * b - 4 * a * c;
                if (disc < 0) continue;
                double sq   = Math.Sqrt(disc);
                double t1   = (-b - sq) / (2 * a);
                double t2   = (-b + sq) / (2 * a);
                // Use the nearest positive t on the segment; fall back to t2 if t1 is behind origin.
                double tHit = (t1 >= 0) ? t1 : t2;
                if (tHit >= 0 && tHit <= 1 && tHit < minT) minT = tHit;
            }
            return minT;
        }
        // Returns the directional sprite matching the current WASD input.
        // Falls back to the last-used direction when no key is held.
        private BitmapImage PickMoveSprite()
        {
            bool n = _wKey, s = _sKey, e = _dKey, w = _aKey;
            if (!n && !s && !e && !w) return _lastMoveSprite;
            if (n && e) return _lastMoveSprite = ShipNE;
            if (n && w) return _lastMoveSprite = ShipNW;
            if (s && e) return _lastMoveSprite = ShipSE;
            if (s && w) return _lastMoveSprite = ShipSW;
            if (n)      return _lastMoveSprite = ShipN;
            if (s)      return _lastMoveSprite = ShipS;
            if (e)      return _lastMoveSprite = ShipE;
            return           _lastMoveSprite = ShipW;
        }

        private void UpdateGun(double cw, double ch)
        {
            double gx   = Model.Gun.GunX;
            double gy   = Model.Gun.GunY;
            double rad  = Model.Gun.Angle * Math.PI / 180.0;
            double tipX = gx + Math.Sin(rad) * BarrelLength;
            double tipY = gy - Math.Cos(rad) * BarrelLength;

            // Update the ship sprite to match the current movement direction.
            _ship.Source = PickMoveSprite();
            Canvas.SetLeft(_ship, gx - ShipSize / 2);
            Canvas.SetTop(_ship,  gy - ShipSize / 2);

            // Blink while invulnerable after a hit.
            _ship.Opacity = (_invuln > 0 && (_invuln / 5) % 2 == 0) ? 0.35 : 1.0;

            if (_muzzleFlash > 0)
            {
                double a   = _muzzleFlash / 3.0;
                double fsz = 24 * a;
                _gunFlash.Visibility = Visibility.Visible;
                _gunFlash.Width  = fsz;
                _gunFlash.Height = fsz;
                _gunFlash.Opacity = a;
                Canvas.SetLeft(_gunFlash, tipX - fsz / 2);
                Canvas.SetTop(_gunFlash,  tipY - fsz / 2);
            }
            else
            {
                _gunFlash.Visibility = Visibility.Collapsed;
            }
        }

        // -- HUD overlay ------------------------------------------------------
        private void UpdateHud(double cw, double ch)
        {
            if (_shownScore != Model.Score)
            {
                _shownScore    = Model.Score;
                _hudScore.Text = $"Score: {Model.Score}";
            }
            if (_shownShapes != Model.Shapes.Count)
            {
                _shownShapes    = Model.Shapes.Count;
                _hudShapes.Text = $"Shapes: {Model.Shapes.Count}";
            }

            // Shield pips: filled while intact, hollow once spent.
            for (int i = 0; i < _shieldPips.Length; i++)
                _shieldPips[i].Fill = i < _health ? Brushes.Aqua : null;

            if (_shownLevel != _level)
            {
                _shownLevel    = _level;
                _hudLevel.Text = $"LEVEL {_level}";
            }
            Canvas.SetLeft(_hudLevel, cw / 2 - 30);

            // Heat gauge: always visible.
            bool   over = Model.Gun.Overheated;
            double heat = Model.Gun.Heat;

            Canvas.SetLeft(_heatBg,   12); Canvas.SetTop(_heatBg,   88);
            Canvas.SetLeft(_heatFill, 12); Canvas.SetTop(_heatFill, 88);
            _heatFill.Width = heat * HeatBarW;
            _heatFill.Fill  = over ? Brushes.Red : heat > 0.6 ? Brushes.Orange : Brushes.LightGreen;

            _heatText.Text       = over ? "OVERHEAT!" : "TORPEDO";
            _heatText.Foreground = over ? Brushes.Red : Brushes.LightGray;
            Canvas.SetLeft(_heatText, 12); Canvas.SetTop(_heatText, 75);

            // Centre banner: game over takes priority, then the inter-level warp.
            if (_gameOver)
            {
                _hudClear.Visibility = Visibility.Visible;
                _hudClear.Foreground = Brushes.OrangeRed;
                _hudClear.Text = $"GAME OVER  \n  reached level {_level}, score {Model.Score}. \n Press Play to restart";
                Canvas.SetLeft(_hudClear, cw / 2 - 285);
                Canvas.SetTop(_hudClear,  ch / 2 - 30);
            }
            else if (_warpPhase == WarpPhase.SlideToBase)
            {
                _hudClear.Visibility = Visibility.Visible;
                _hudClear.Foreground = Brushes.GreenYellow;
                _hudClear.Text = $"LEVEL {_level} CLEARED  -  engaging warp drive...";
                Canvas.SetLeft(_hudClear, cw / 2 - 230);
                Canvas.SetTop(_hudClear,  ch / 2 - 30);
            }
            else if (_warpPhase == WarpPhase.Hyperspace)
            {
                _hudClear.Visibility = Visibility.Visible;
                _hudClear.Foreground = Brushes.Cyan;
                _hudClear.Text = $">> ENTERING LEVEL {_level + 1} <<";
                Canvas.SetLeft(_hudClear, cw / 2 - 160);
                Canvas.SetTop(_hudClear,  ch / 2 - 30);
            }
            else
            {
                _hudClear.Visibility = Visibility.Collapsed;
            }
        }

        // --------------------------------------------------------------------
        //  KEYBOARD
        // --------------------------------------------------------------------
		private void Window_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				if (_paused) ClosePauseMenu();
				else         OpenPauseMenu();
				e.Handled = true;
				return;
			}
			if (_paused) return;
			switch (e.Key)
			{
                case Key.A:
                    _aKey     = true;
                    e.Handled = true;
                    break;
                case Key.D:
                    _dKey     = true;
                    e.Handled = true;
                    break;
                case Key.W:
                    _wKey     = true;
                    e.Handled = true;
                    break;
                case Key.S:
                    _sKey     = true;
                    e.Handled = true;
                    break;
                case Key.Space:
                    _fireKeyHeld = true;
                    e.Handled    = true;
                    // Torpedo fires once per press; phasers are handled continuously by Step()
                    if (!e.IsRepeat && _warpPhase == WarpPhase.None && Model.Gun.CanFire())
                    {
                        FireBullet();
                        Model.Gun.ResetFireCooldown();
                    }
                    break;
            }
        }

		private void Window_KeyUp(object sender, KeyEventArgs e)
		{
			switch (e.Key)
			{
				case Key.A:     _aKey        = false; e.Handled = true; break;
				case Key.D:     _dKey        = false; e.Handled = true; break;
				case Key.W:     _wKey        = false; e.Handled = true; break;
				case Key.S:     _sKey        = false; e.Handled = true; break;
				case Key.Space: _fireKeyHeld = false; e.Handled = true; break;
			}
		}

		// Window lost focus (alt-tab, click another app) while keys were held:
		// their KeyUp goes to the other window, so clear everything to avoid a
		// latched key (e.g. the ship drifting or the machine gun firing forever).
		private void Window_Deactivated(object? sender, EventArgs e)
		{
			_aKey = _dKey = _wKey = _sKey = false;
			_fireKeyHeld = _fireMouseHeld = false;
		}

		// --------------------------------------------------------------------
		//  PAUSE MENU
		// --------------------------------------------------------------------
		private void OpenPauseMenu()
		{
			_paused     = true;
			_wasRunning = _running;
			StopLoop();
			_aKey = _dKey = _wKey = _sKey = false;
			_fireKeyHeld = _fireMouseHeld = false;
			PauseOverlay.Visibility = Visibility.Visible;
		}

		private void ClosePauseMenu()
		{
			_paused = false;
			PauseOverlay.Visibility = Visibility.Collapsed;
			if (_wasRunning) StartLoop();
		}

		private void ResumeButton_Click(object sender, RoutedEventArgs e) => ClosePauseMenu();

		private void MusicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			_audio.MusicVolume = e.NewValue / 100.0;
			if (MusicPct != null) MusicPct.Text = $"{(int)e.NewValue}%";
		}

		private void SfxSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
		{
			_audio.SfxVolume = e.NewValue / 100.0;
			if (SfxPct != null) SfxPct.Text = $"{(int)e.NewValue}%";
		}

        // --------------------------------------------------------------------
        //  TOOLBAR HANDLERS
        // --------------------------------------------------------------------
        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameOver) NewGame();   // a fresh game after defeat
            StartLoop();
        }
        private void RotateLeft_Click(object sender, RoutedEventArgs e)
        {
            Model.Gun.Angle = WrapAngle(Model.Gun.Angle - 10);
            if (!_running) Render();
        }

        private void RotateRight_Click(object sender, RoutedEventArgs e)
        {
            Model.Gun.Angle = WrapAngle(Model.Gun.Angle + 10);
            if (!_running) Render();
        }

        // Keep the angle in (-180, 180] so it never drifts off to large values.
        private static double WrapAngle(double deg)
        {
            deg %= 360.0;
            if (deg > 180.0)  deg -= 360.0;
            if (deg <= -180.0) deg += 360.0;
            return deg;
        }

        // -- Mouse handlers ---------------------------------------------------
        private void ShapeCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point  mp = e.GetPosition(ShapeCanvas);

            // Aim toward the cursor in any direction (full 360deg).
            double dx = mp.X - Model.Gun.GunX;
            double dy = Model.Gun.GunY - mp.Y;       // positive = cursor above ship
            Model.Gun.Angle = Math.Atan2(dx, dy) * 180.0 / Math.PI;

            // Move the drawn crosshair to the cursor (and show it if a MouseEnter
            // was missed because the cursor was already over the canvas at start).
            Canvas.SetLeft(_reticle, mp.X);
            Canvas.SetTop(_reticle,  mp.Y);
            if (_reticle.Visibility != Visibility.Visible)
                _reticle.Visibility = Visibility.Visible;

            if (!_running) Render();
        }

        private void ShapeCanvas_MouseEnter(object sender, MouseEventArgs e)
            => _reticle.Visibility = Visibility.Visible;

        private void ShapeCanvas_MouseLeave(object sender, MouseEventArgs e)
            => _reticle.Visibility = Visibility.Collapsed;

        private void ShapeCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _fireMouseHeld = true;
            ShapeCanvas.CaptureMouse();
            this.Focus();
        }

        private void ShapeCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _fireMouseHeld = false;
            if (ShapeCanvas.IsMouseCaptured) ShapeCanvas.ReleaseMouseCapture();
        }

        private void ShapeCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Right click fires a torpedo
            if (_warpPhase == WarpPhase.None && Model.Gun.CanFire())
            {
                FireBullet();
                Model.Gun.ResetFireCooldown();
                if (!_running) Render();
            }
            e.Handled = true;
            this.Focus();
        }

        private void ShapeCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        // Capture can also be lost without a button-up (e.g. focus stolen);
        // clear the hold so fire never latches on.
        private void ShapeCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _fireMouseHeld = false;
        }

            }
        }

