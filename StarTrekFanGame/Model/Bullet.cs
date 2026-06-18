namespace StarTrekFanGame.Model
{
    /// <summary>A projectile fired from the gun.</summary>
    class Bullet
    {
        public double X, Y;
        public double VX, VY;          // velocity (pixels / tick)
        public bool   IsActive = true;
    }
}
