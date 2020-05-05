using System;
using TabletDriverPlugin;
using TabletDriverPlugin.Attributes;
using TabletDriverPlugin.Tablet;

namespace OpenTabletDriverPlugins
{
    using static Math;

    [PluginName("Anti-Smoothing")]
    public class AntiSmoothingFilter : Notifier, IFilter
    {
        public Point Filter(Point point)
        {
            TimeSpan timeDelta = _lastTime - DateTime.Now;
            
            if (timeDelta.Milliseconds >= 1 && timeDelta.Milliseconds <= 10)
            {
                LimitReportRate();
                _reportRateAvg += ((1000f / timeDelta.Ticks) - _reportRateAvg) * (timeDelta.Ticks / 1000f ) * 10;
                if (_reportRateAvg > _reportRate)
                    _reportRate = _reportRateAvg;
            }
            _lastTime = DateTime.Now;

            float velocity = point.DistanceFrom(_lastPoint ?? point);
            float acceleration = (velocity - _oldVelocity) * _reportRate;

            if (velocity < 1)
                return point;
            
            float predictedVelocity = velocity + acceleration / _reportRate;

            Point predicted;
            if (velocity > 0.1 && predictedVelocity > 0.1 && velocity < 2000 && predictedVelocity < 2000)
            {
                predicted = new Point(_lastPoint.X, _lastPoint.Y);
                double shapedVelocityFactor = Pow(predictedVelocity / velocity, Shape) * Compensation;

                predicted.X += (float)(point.X + shapedVelocityFactor * Compensation * (_reportRate / 1000f));
                predicted.Y += (float)(point.Y + shapedVelocityFactor * Compensation * (_reportRate / 1000f));                
            }
            else
            {
                predicted = point;
            }

            _oldVelocity = velocity;
            
            _lastPoint = point;
            return predicted;
        }

        private void LimitReportRate()
        {
            if (_reportRate < 100)
                _reportRate = 100;
            else if (_reportRate > 1000)
                _reportRate = 1000;
            if (_reportRateAvg < 100)
                _reportRateAvg = 100;
            else if (_reportRateAvg > 1000)
                _reportRateAvg = 1000;
        }


        private Point _lastPoint = null;
        private DateTime _lastTime = DateTime.Now;
        private float _reportRate, _reportRateAvg, _oldVelocity;

        private float _velocity, _shape, _compensation;

        [UnitProperty("Velocity", "px/s")]
        public float Velocity
        {
            set => this.RaiseAndSetIfChanged(ref _velocity, value);
            get => _velocity;
        }

        [Property("Shape")]
        public float Shape
        {
            set => this.RaiseAndSetIfChanged(ref _shape, value);
            get => _shape;
        }

        [UnitProperty("Compensation", "ms")]
        public float Compensation
        {
            set => this.RaiseAndSetIfChanged(ref _compensation, value);
            get => _compensation;
        }

        public FilterStage FilterStage => FilterStage.PostTranspose;
    }
}