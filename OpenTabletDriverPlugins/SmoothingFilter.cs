using System;
using TabletDriverPlugin;
using TabletDriverPlugin.Attributes;
using TabletDriverPlugin.Tablet;

namespace OpenTabletDriverPlugins
{
    [PluginName("Smoothing")]
    public class SmoothingFilter : Notifier, IFilter
    {
        public virtual Point Filter(Point point)
        {
            var pt = new Point(point.X, point.Y);
            var deltaTime = DateTime.Now - time;
            if (lastpos != null && deltaTime.Milliseconds < ResetInterval)
            {
                Point delta = lastpos - pt;
                pt.X += delta.X / deltaTime.Milliseconds * Weight;
                pt.Y += delta.Y / deltaTime.Milliseconds * Weight;
            }


            if (float.IsInfinity(pt.X) || float.IsInfinity(pt.Y))
                return lastpos;
            
            lastpos = pt;
            time = DateTime.Now;
            return pt;
        }

        protected DateTime time;
        protected Point lastpos;
        
        private float _weight;
        [SliderProperty("Weight", 0, 5, 2.5f)]
        public float Weight
        {
            set => this.RaiseAndSetIfChanged(ref _weight, value);
            get => _weight;
        }

        private int _resetInterval;
        [SliderProperty("Reset Interval", 1, 1000, 100)]
        public int ResetInterval
        {
            set => this.RaiseAndSetIfChanged(ref _resetInterval, value);
            get => _resetInterval;
        }
    }
}