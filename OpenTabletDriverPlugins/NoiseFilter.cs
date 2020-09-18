using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriverPlugins
{
    [PluginName("Anti-Noise")]
    public class NoiseFilter : Notifier, IFilter
    {
        public Vector2 Filter(Vector2 point)
        {
            var pt = new Vector2(point.X, point.Y);
            if (_lastpos.HasValue)
            {
                var distance = Vector2.Distance(point, _lastpos.Value);
                if (distance < DistanceThreshold)
                    pt = new Vector2(_lastpos.Value.X, _lastpos.Value.Y);
                // Log.Debug($"Distance {distance} | Threshold {DistanceThreshold}");
            }
            _lastpos = pt;
            return pt;
        }
        
        protected Vector2? _lastpos;


        [SliderProperty("Distance Threshold", 0, 5, 2.5f)]
        public float DistanceThreshold { get; set; }

        public FilterStage FilterStage => FilterStage.PostTranspose;
    }
}