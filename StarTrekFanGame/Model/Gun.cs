namespace StarTrekFanGame.Model
{
    /// <summary>
    /// Represents the player's cannon: angle, fire-rate cooldown, and torpedo heat.
    /// Angle 0 = straight up; positive = clockwise (right).
    /// Both phasers and torpedoes are always available simultaneously.
    /// </summary>
    class Gun
    {
        public double Angle = 0.0;   // degrees; 0 = straight up
        public double GunX  = 0.0;   // horizontal position on canvas (pixels)
        public double GunY  = 0.0;   // vertical position on canvas (pixels)

        // Torpedo fires every 12 ticks (~400 ms at 30 FPS); phasers are continuous.
        public int FireRate => 12;

        private int _cooldown = 0;

        // -- Photon-torpedo overheat (0 = cool, 1 = fully overheated) ---------
        public double Heat       = 0.0;
        public bool   Overheated = false;

        private const double HeatPerShot  = 0.22;   // added per torpedo shot
        private const double CoolPerTick  = 0.008;  // dissipated every tick
        private const double RecoverBelow = 0.15;   // can fire again once cooled to here

        // Torpedo is locked out while overheated.
        public bool CanFire()           => _cooldown <= 0 && !Overheated;
        public void ResetFireCooldown() => _cooldown = FireRate;

        /// <summary>Builds heat for one torpedo shot.</summary>
        public void AddHeat()
        {
            Heat += HeatPerShot;
            if (Heat >= 1.0) { Heat = 1.0; Overheated = true; }
        }

        public void ResetHeat()
        {
            Heat = 0.0;
            Overheated = false;
        }

        public void Tick()
        {
            if (_cooldown > 0) _cooldown--;

            if (Heat > 0.0) Heat = System.Math.Max(0.0, Heat - CoolPerTick);
            if (Overheated && Heat <= RecoverBelow) Overheated = false;
        }
    }
}
