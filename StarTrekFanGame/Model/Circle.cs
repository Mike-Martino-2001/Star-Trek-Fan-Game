namespace StarTrekFanGame.Model
{
    class Circle : GameShape
    {
        public double Radius;
        public override double CollisionRadius => Radius;

        public Circle(double x, double y, double radius)
        {
            X = x; Y = y; Radius = radius;
        }
    }
}
