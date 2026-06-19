namespace StarTrekFanGame.Model
{
    /// <summary>
    /// A green energy projectile fired by a Fighter enemy toward the player.
    /// </summary>
    class EnemyBullet
    {
        public double X, Y;
        public double VX, VY;
        public bool   IsActive = true;
    }
}
