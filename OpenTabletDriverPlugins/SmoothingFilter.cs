using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriverPlugins
{
    [PluginName("Smoothing")]
    public class SmoothingFilter : Notifier, IFilter
    {
        public virtual Vector2 Filter(Vector2 point)
        {
            var pt = new Vector2(point.X, point.Y);
            var deltaTime = DateTime.Now - time;
            if (lastpos.HasValue && deltaTime.Milliseconds < ResetInterval)
            {
                Vector2 delta = lastpos.Value - pt;
                pt.X += delta.X / deltaTime.Milliseconds * Weight;
                pt.Y += delta.Y / deltaTime.Milliseconds * Weight;
            }


            if (float.IsInfinity(pt.X) || float.IsInfinity(pt.Y))
                return lastpos.Value;

            lastpos = pt;
            time = DateTime.Now;
            return pt;
        }

        public FilterStage FilterStage => FilterStage.PostTranspose;

        protected DateTime time;
        protected Vector2? lastpos = null;

        [SliderProperty("Weight", 0, 5, 2.5f)]
        public float Weight { get; set; }

        [SliderProperty("Reset Interval", 1, 1000, 100)]
        public int ResetInterval { get; set; }
    }
}