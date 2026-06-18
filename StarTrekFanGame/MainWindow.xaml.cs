using StarTrekFanGame.Model;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private int  _warpCountdown = 0;        // frames until the next level loads

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

        // -- Retained visuals -------------------------------------------------
        //  Each game object owns one WPF element that is created once and then
        //  only repositioned each frame. This avoids rebuilding the whole visual
        //  tree (and the GC pressure that caused the periodic stutter).
        private sealed class BulletVisual { public Line Trail = null!; public Image Sprite = null!; }

        private readonly Dictionary<GameShape, FrameworkElement> _shapeVisuals    = new();
        private readonly Dictionary<ExplosionParticle, Ellipse>  _particleVisuals = new();
        private readonly Dictionary<Bullet, BulletVisual>        _bulletVisuals   = new();

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

        // Free-running frame counter, drives the continuous low-health flash.
        private int _frame;

        // Last values pushed to the HUD text blocks. The HUD updates every frame
        // but these change rarely, so we skip the string interpolation (and its
        // per-frame allocations) unless the underlying value actually moved.
        private int  _shownScore  = int.MinValue;
        private int  _shownShapes = int.MinValue;
        private int  _shownLevel  = int.MinValue;
        private bool _shownMg     = false;
        private bool _shownMgInit = false;

        // Persistent "chrome" visuals (player ship + HUD), created once in the ctor.
        private BitmapImage _lastMoveSprite = ShipN;
        private readonly Image _ship = new() { Width = ShipSize, Height = ShipSize, Source = ShipN };
        private readonly Ellipse   _gunFlash  = new() { Visibility = Visibility.Collapsed };
        private readonly TextBlock _hudScore  = new() { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.Yellow };
        private readonly TextBlock _hudShapes = new() { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.LightGray };
        private readonly TextBlock _hudShields = new() { FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.Aqua };
        private readonly Polygon[] _shieldPips = new Polygon[MaxHealth];
        private readonly TextBlock _hudLevel  = new() { FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Aqua };
        private readonly TextBlock _hudMode   = new() { FontSize = 12, FontWeight = FontWeights.Bold };
        private readonly TextBlock _hudHint   = new() { FontSize = 10, FontWeight = FontWeights.Bold };
        private readonly TextBlock _hudClear  = new() { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Brushes.GreenYellow, Visibility = Visibility.Collapsed };

        // Machine-gun heat gauge (top-right, only shown in machine-gun mode).
        private const double HeatBarW = 150.0;
        private readonly Rectangle _heatBg   = new() { Width = HeatBarW, Height = 9, Stroke = Brushes.Gray, StrokeThickness = 1, Fill = Frozen(90, 0, 0, 0) };
        private readonly Rectangle _heatFill = new() { Height = 9 };
        private readonly TextBlock _heatText = new() { FontSize = 10, FontWeight = FontWeights.Bold };

        // Z-order layers (Canvas draws by ZIndex, so insertion order no longer matters).
        private const int ZShape = 0, ZParticle = 1, ZBullet = 2, ZGun = 3, ZHud = 4, ZReticle = 5;

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
        }

        // Create the persistent player-ship + HUD visuals exactly once.
        private void BuildChrome()
        {
            _gunFlash.Fill = FlashFill;
            _hudHint.Foreground = HintFill;
            _hudHint.Text  = "WASD Move   |   Mouse Aim   |   Click / Space = Shoot";

            // Keep the pixel-art ship crisp.
            RenderOptions.SetBitmapScalingMode(_ship, BitmapScalingMode.NearestNeighbor);

            AddChrome(_ship,      ZGun);
            AddChrome(_gunFlash,  ZGun);
            AddChrome(_hudScore,  ZHud);
            AddChrome(_hudShapes, ZHud);
            AddChrome(_hudShields, ZHud);
            AddChrome(_hudLevel,  ZHud);
            AddChrome(_hudMode,   ZHud);
            AddChrome(_hudHint,   ZHud);
            AddChrome(_heatBg,    ZHud);
            AddChrome(_heatFill,  ZHud);   // drawn over the background bar
            AddChrome(_heatText,  ZHud);
            AddChrome(_hudClear,  ZHud);

            // Fixed-position HUD lines
            Canvas.SetLeft(_hudScore, 12);  Canvas.SetTop(_hudScore, 10);
            Canvas.SetLeft(_hudShapes, 12); Canvas.SetTop(_hudShapes, 33);
            Canvas.SetTop(_hudMode, 10);
            Canvas.SetTop(_hudHint, 28);

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
            _audio.StartTheme();   // plays for the whole session, looping with a 10 s gap
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
            ScoreText.Text = "Score: 0";
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
            _level         = level;
            _levelStarted  = true;
            _warpCountdown = 0;
            ShapeCanvas.Background = GetBackground((level - 1) % BackgroundFiles.Length);
            SpawnShapes(4 + level * 2);   // more targets each level
        }

        // Remove every dynamic entity (shapes / bullets / particles) and its visual.
        private void ClearField()
        {
            Model.Shapes.Clear();
            Model.Bullets.Clear();
            Model.Particles.Clear();

            foreach (var v in _shapeVisuals.Values)     ShapeCanvas.Children.Remove(v);
            foreach (var e2 in _particleVisuals.Values) ShapeCanvas.Children.Remove(e2);
            foreach (var bv in _bulletVisuals.Values)
            {
                ShapeCanvas.Children.Remove(bv.Trail);
                ShapeCanvas.Children.Remove(bv.Sprite);
            }
            _shapeVisuals.Clear();
            _particleVisuals.Clear();
            _bulletVisuals.Clear();
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

            // -- 1. Move ship (WASD keys) -------------------------------------
            if (_aKey) Model.Gun.GunX -= GunMoveSpeed;
            if (_dKey) Model.Gun.GunX += GunMoveSpeed;
            if (_wKey) Model.Gun.GunY -= GunMoveSpeed;
            if (_sKey) Model.Gun.GunY += GunMoveSpeed;

            double minY = ShipSize / 2 + 6;
            double maxY = ch - GunBaseOffset;
            Model.Gun.GunX = Math.Clamp(Model.Gun.GunX, 40, cw - 40);
            Model.Gun.GunY = Math.Clamp(Model.Gun.GunY, minY, maxY);

            // -- 2. Machine-gun auto-fire -------------------------------------
            Model.Gun.Tick();
            if (FireHeld && Model.Gun.MachineGunMode && Model.Gun.CanFire())
            {
                FireBullet();
                Model.Gun.ResetFireCooldown();
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
                        shape.Hits--;

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
                        Model.Score += 100;
                        ScoreText.Text = $"Score: {Model.Score}";
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

            // -- 8b. Level progression ----------------------------------------
            // When the field is cleared, pause briefly ("warp") then load the
            // next level with its own background and more shapes.
            if (_levelStarted && Model.Shapes.Count == 0 && _warpCountdown == 0)
                _warpCountdown = 120;               // ~2 s at 60 FPS

            if (_warpCountdown > 0 && --_warpCountdown == 0)
                StartLevel(_level + 1);
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
            Model.Gun.AddHeat();      // machine-gun builds heat; rifle ignores it
            _audio.PlayTorpedo();
        }

        private void CreateExplosion(double x, double y)
        {
            const int N = 22;
            for (int i = 0; i < N; i++)
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
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

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
                    e = new Ellipse { Fill = ParticleFill };
                    _particleVisuals[p] = e;
                    Panel.SetZIndex(e, ZParticle);
                    ShapeCanvas.Children.Add(e);
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
                ShapeCanvas.Children.Remove(e);
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
                    var sprite = new Image
                    {
                        Width = tw, Height = TorpedoSize, Source = TorpedoSprite,
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        // Torpedo art points "up"; rotate it onto the flight path.
                        RenderTransform = new RotateTransform(
                            Math.Atan2(b.VX, -b.VY) * 180.0 / Math.PI)
                    };
                    RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);

                    bv = new BulletVisual
                    {
                        Trail  = new Line { Stroke = Brushes.Yellow, StrokeThickness = 1.5, Opacity = 0.7 },
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

        // -- Player ship visual -----------------------------------------------
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
            bool mg = Model.Gun.MachineGunMode;

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

            if (!_shownMgInit || _shownMg != mg)
            {
                _shownMgInit        = true;
                _shownMg            = mg;
                _hudMode.Text       = mg ? "MACHINE GUN" : "RIFLE";
                _hudMode.Foreground = mg ? Brushes.OrangeRed : Brushes.LightGreen;
            }
            Canvas.SetLeft(_hudMode, cw - 135);

            Canvas.SetLeft(_hudHint, cw - 270);

            // Heat gauge: only relevant for the machine gun.
            Visibility heatVis = mg ? Visibility.Visible : Visibility.Collapsed;
            _heatBg.Visibility = _heatFill.Visibility = _heatText.Visibility = heatVis;
            if (mg)
            {
                bool   over = Model.Gun.Overheated;
                double heat = Model.Gun.Heat;

                Canvas.SetLeft(_heatBg,   12); Canvas.SetTop(_heatBg,   88);
                Canvas.SetLeft(_heatFill, 12); Canvas.SetTop(_heatFill, 88);
                _heatFill.Width = heat * HeatBarW;
                _heatFill.Fill  = over ? Brushes.Red : heat > 0.6 ? Brushes.Orange : Brushes.Cyan;

                _heatText.Text       = over ? "OVERHEAT!" : "HEAT";
                _heatText.Foreground = over ? Brushes.Red : Brushes.LightGray;
                Canvas.SetLeft(_heatText, 12); Canvas.SetTop(_heatText, 75);
            }

            // Centre banner: game over takes priority, then the inter-level warp.
            if (_gameOver)
            {
                _hudClear.Visibility = Visibility.Visible;
                _hudClear.Foreground = Brushes.OrangeRed;
                _hudClear.Text = $"GAME OVER  \n  reached level {_level}, score {Model.Score}. \n Press Play to restart";
                Canvas.SetLeft(_hudClear, cw / 2 - 285);
                Canvas.SetTop(_hudClear,  ch / 2 - 30);
            }
            else if (_warpCountdown > 0)
            {
                _hudClear.Visibility = Visibility.Visible;
                _hudClear.Foreground = Brushes.GreenYellow;
                _hudClear.Text = $"LEVEL {_level} CLEARED  -  warping to level {_level + 1}...";
                Canvas.SetLeft(_hudClear, cw / 2 - 230);
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
                    // Rifle: fire once per press (ignore key-repeat)
                    if (!e.IsRepeat && !Model.Gun.MachineGunMode && Model.Gun.CanFire())
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
        private void SpawnButton_Click(object sender, RoutedEventArgs e)
        {
            SpawnShapes(6);
            Render();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            NewGame();   // wipe the field, restore shields, restart at level 1
            Render();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameOver) NewGame();   // a fresh game after defeat
            StartLoop();
        }
        private void StepButton_Click(object sender, RoutedEventArgs e)  => StepOnce();
        private void StopButton_Click(object sender, RoutedEventArgs e)  => StopLoop();

        private void ShootButton_Click(object sender, RoutedEventArgs e)
        {
            if (Model.Gun.CanFire())
            {
                FireBullet();
                Model.Gun.ResetFireCooldown();
                if (!_running) Render();
            }
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
            // Capture the mouse so the matching button-up is always delivered to
            // the canvas, even if the cursor leaves it while held. Without this the
            // release is lost and the machine gun keeps firing until the next click.
            ShapeCanvas.CaptureMouse();

            // Rifle fires immediately on click; machine gun is handled by Step()
            if (!Model.Gun.MachineGunMode && Model.Gun.CanFire())
            {
                FireBullet();
                Model.Gun.ResetFireCooldown();
                if (!_running) Render();
            }
            // Keep focus on window so A/D keyboard still works
            this.Focus();
        }

        private void ShapeCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _fireMouseHeld = false;
            if (ShapeCanvas.IsMouseCaptured) ShapeCanvas.ReleaseMouseCapture();
        }

        // Capture can also be lost without a button-up (e.g. focus stolen);
        // clear the hold so fire never latches on.
        private void ShapeCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _fireMouseHeld = false;
        }

        private void RifleButton_Click(object sender, RoutedEventArgs e)
        {
            Model.Gun.MachineGunMode    = false;
            Model.Gun.ResetHeat();
            RifleButton.FontWeight      = FontWeights.Bold;
            MachineGunButton.FontWeight = FontWeights.Normal;
            if (!_running) Render();
        }

        private void MachineGunButton_Click(object sender, RoutedEventArgs e)
        {
            Model.Gun.MachineGunMode    = true;
            Model.Gun.ResetHeat();
            MachineGunButton.FontWeight = FontWeights.Bold;
            RifleButton.FontWeight      = FontWeights.Normal;
            if (!_running) Render();
        }
    }
}

