using TabletDriverPlugin;
using TabletDriverPlugin.Attributes;
using TabletDriverPlugin.Tablet;

namespace OpenTabletDriverPlugins
{
    [PluginName("Anti-Noise")]
    public class NoiseFilter : Notifier, IFilter
    {
        public Point Filter(Point point)
        {
            var pt = new Point(point.X, point.Y);
            if (_lastpos != null)
            {
                var distance = point.DistanceFrom(_lastpos);
                if (distance < DistanceThreshold)
                    pt = new Point(_lastpos.X, _lastpos.Y);
                // Log.Debug($"Distance {distance} | Threshold {DistanceThreshold}");
            }
            _lastpos = pt;
            return pt;
        }
        
        protected Point _lastpos;

        private float _threshold;

        [SliderProperty("Distance Threshold", 0, 5, 2.5f)]
        public float DistanceThreshold
        {
            set => this.RaiseAndSetIfChanged(ref _threshold, value);
            get => _threshold;
        }

        public FilterStage FilterStage => FilterStage.PostTranspose;
    }
}