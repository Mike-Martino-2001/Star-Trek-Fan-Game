using System;

namespace StarTrekFanGame.Model
{
    class RectShape : GameShape
    {
        public double Width, Height;
        public override double CollisionRadius => Math.Sqrt(Width * Width + Height * Height) / 2.0;

        public RectShape(double x, double y, double width, double height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
    }
}
