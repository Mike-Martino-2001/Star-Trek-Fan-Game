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

        /// <summary>Radius of the bounding circle used for collision detection.</summary>
        public abstract double CollisionRadius { get; }
    }
}
