using System.Collections.Generic;

namespace StarTrekFanGame.Model
{
    class GameModel
    {
        public List<GameShape>         Shapes       = new();
        public List<Bullet>            Bullets      = new();
        public List<EnemyBullet>       EnemyBullets = new();
        public List<Powerup>           Powerups     = new();
        public List<ExplosionParticle> Particles    = new();
        public Gun                     Gun          = new Gun();
        public int                     Score        = 0;
    }
}
