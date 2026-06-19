namespace StarTrekFanGame.Model
{
    /// <summary>
    /// A warp fuel cell pickup dropped by a destroyed Spawner-tier enemy.
    /// Drifts slowly downward; collected on player contact.
    /// </summary>
    class Powerup
    {
        public double X, Y;
        public bool   IsActive = true;

        // Animation state
        public int  Frame    = 0;   // current sprite frame index
        public int  FrameTick = 0;  // ticks elapsed on the current frame

        public const double CollisionRadius = 22.0;
        public const double DriftSpeed      = 0.4;   // px/tick downward drift
        public const int    FrameInterval   = 6;     // ticks per animation frame
    }
}
