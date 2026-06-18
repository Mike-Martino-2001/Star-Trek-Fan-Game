namespace StarTrekFanGame.Model
{
    /// <summary>
    /// Represents the player's cannon: angle, fire mode, and fire-rate cooldown.
    /// Angle 0 = straight up; positive = clockwise (right).
    /// </summary>
    class Gun
    {
        public double Angle          = 0.0;   // degrees; 0 = straight up
        public double GunX           = 0.0;   // horizontal position on canvas (pixels)
        public double GunY           = 0.0;   // vertical position on canvas (pixels)
        public bool   MachineGunMode = false;

        // Rifle fires every 12 ticks (~400 ms at 30 FPS); machine-gun every 6 ticks.
        public int FireRate => MachineGunMode ? 6 : 12;

        private int _cooldown = 0;

        // -- Machine-gun overheat (0 = cool, 1 = fully overheated) ------------
        public double Heat       = 0.0;
        public bool   Overheated = false;

        private const double HeatPerShot  = 0.15;   // added each machine-gun shot
        private const double CoolPerTick  = 0.010;  // dissipated every tick
        private const double RecoverBelow = 0.0;   // can fire again once cooled to here

        // The machine gun is locked out while overheated; the rifle never overheats.
        public bool CanFire()           => _cooldown <= 0 && !(MachineGunMode && Overheated);
        public void ResetFireCooldown() => _cooldown = FireRate;

        /// <summary>Builds heat for one shot (machine-gun mode only).</summary>
        public void AddHeat()
        {
            if (!MachineGunMode) return;
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
