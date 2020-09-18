using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriverPlugins
{
    using static Math;

    [PluginName("Anti-Smoothing")]
    public class AntiSmoothingFilter : Notifier, IFilter
    {
        public Vector2 Filter(Vector2 point)
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

            float velocity = Vector2.Distance(point, _lastPoint ?? point);
            float acceleration = (velocity - _oldVelocity) * _reportRate;

            if (velocity < 1)
            {
                _lastPoint = point;
                return point;
            }
            
            float predictedVelocity = velocity + acceleration / _reportRate;

            Vector2 predicted;
            if (velocity > 0.1 && predictedVelocity > 0.1 && velocity < 2000 && predictedVelocity < 2000)
            {
                predicted = new Vector2(_lastPoint.Value.X, _lastPoint.Value.Y);
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


        private Vector2? _lastPoint = null;
        private DateTime _lastTime = DateTime.Now;
        private float _reportRate, _reportRateAvg, _oldVelocity;


        [UnitProperty("Velocity", "px/s")]
        public float Velocity { get; set; }

        [Property("Shape")]
        public float Shape { get; set; }

        [UnitProperty("Compensation", "ms")]
        public float Compensation { get; set; }

        public FilterStage FilterStage => FilterStage.PostTranspose;
    }
}