namespace StarTrekFanGame.Model
{
    /// <summary>One spark emitted when a shape is destroyed.</summary>
    class ExplosionParticle
    {
        public double X, Y;
        public double VX, VY;
        public int Life;     // remaining frames
        public int MaxLife;  // initial life (used to compute fade ratio)
    }
}
