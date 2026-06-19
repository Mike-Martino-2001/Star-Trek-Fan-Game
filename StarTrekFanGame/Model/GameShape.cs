namespace StarTrekFanGame.Model
{
    /// <summary>
    /// Abstract base for every bouncing game shape.
    /// All positions are the shape's visual centre.
    /// </summary>
    abstract class GameShape
    {
        public double X, Y;    // centre position (pixels)
        public double VX, VY;  // velocity       (pixels / tick)

        public int Hits;       // bullet hits remaining before destruction
        public int MaxHits;    // initial hit count (drives the damage visual)
        public int HitFlash;   // frames remaining of the red damage flash

        // -- Enemy behaviour --------------------------------------------------
        /// <summary>Role assigned at spawn based on size tier.</summary>
        public EnemyRole Role = EnemyRole.None;

        /// <summary>Ticks until next action (shoot / spawn).</summary>
        public int ActionCooldown = 0;

        /// <summary>True while a shield generator is covering this enemy.</summary>
        public bool IsShielded = false;

        /// <summary>Radius of the bounding circle used for collision detection.</summary>
        public abstract double CollisionRadius { get; }
    }

    enum EnemyRole
    {
        None,           // not yet assigned
        ShieldGenerator,// two smallest tiers: generates shields for nearby enemies
        Fighter,        // medium tier: fires green energy spread at the player
        Spawner         // largest tier: periodically spawns new fighter enemies
    }
}
