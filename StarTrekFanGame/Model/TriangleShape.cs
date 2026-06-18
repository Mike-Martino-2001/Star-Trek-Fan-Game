namespace StarTrekFanGame.Model
{
    class TriangleShape : GameShape
    {
        public double Size;  // circumradius
        public override double CollisionRadius => Size;

        public TriangleShape(double x, double y, double size)
        {
            X = x; Y = y; Size = size;
        }
    }
}
