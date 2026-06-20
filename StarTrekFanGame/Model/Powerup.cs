namespace StarTrekFanGame.Model
{
    /// <summary>
    /// A warp fuel cell pickup dropped by a destroyed Spawner-tier enemy.
    /// Drifts slowly downward; collected on player contact.
    /// </summary>
    class Powerup
    {
        public double X, Y;
        public double VX, VY;     // inherits enemy velocity; bounces off screen edges
        public bool   IsActive = true;

        // Animation state
        public int  Frame     = 0;   // current sprite frame index
        public int  FrameTick = 0;   // ticks elapsed on the current frame

        public const double CollisionRadius = 22.0;
        public const int    FrameInterval   = 6;     // ticks per animation frame
    }
}
