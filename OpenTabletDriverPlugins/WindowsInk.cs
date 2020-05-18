using System.Collections.Generic;
using System.Linq;
using TabletDriverPlugin;
using TabletDriverPlugin.Attributes;
using TabletDriverPlugin.Tablet;
using TabletDriverPlugin.Platform.Display;
using HidSharp;
using System;
using System.Runtime.InteropServices;
using TabletDriverPlugin.Logging;

namespace TabletDriverPlugins
{
    [PluginName("Windows Ink")]
    public class AbsoluteInk : IAbsoluteMode
    {
        private IEnumerable<HidDevice> Devices => DeviceList.Local.GetHidDevices();
        private HidDevice VirtualTablet { set; get; }

        private Boolean isOpen = false;

        public InkReport VirtualReport;

        public virtual HidStream ReportStream { protected set; get; }

        public void Read(IDeviceReport report)
        {
            if (!isOpen)
                GetVirtualDevice();
            if (isOpen)
                if (report is ITabletReport tabletReport)
                    Position(tabletReport);
        }

        public Area _displayArea, _tabletArea;
        public IDisplay _selectedDisplay;
        public TabletProperties _tabletProperties;

        public Area Output
        {
            set
            {
                _displayArea = value;
                UpdateCache();
            }
            get => _displayArea;
        }

        public Area Input
        {
            set
            {
                _tabletArea = value;
                UpdateCache();
            }
            get => _tabletArea;
        }

        public IDisplay SelectedDisplay
        {
            set
            {
                _selectedDisplay = value;
                UpdateCache();
            }
            get => _selectedDisplay;
        }

        public TabletProperties TabletProperties
        {
            set
            {
                _tabletProperties = value;
                UpdateCache();
            }
            get => _tabletProperties;
        }

        private IEnumerable<IFilter> _filters, _preFilters, _postFilters;
        public IEnumerable<IFilter> Filters
        {
            set
            {
                _filters = value;
                _preFilters = value.Where(f => f.FilterStage == FilterStage.PreTranspose);
                _postFilters = value.Where(f => f.FilterStage == FilterStage.PostTranspose);
            }
            get => _filters;
        }

        public bool AreaClipping { set; get; }

        private void UpdateCache()
        {
            _rotationMatrix = Input?.GetRotationMatrix();

            _halfDisplayWidth = Output?.Width / 2 ?? 0;
            _halfDisplayHeight = Output?.Height / 2 ?? 0;
            _halfTabletWidth = Input?.Width / 2 ?? 0;
            _halfTabletHeight = Input?.Height / 2 ?? 0;

            _minX = Output?.Position.X - _halfDisplayWidth ?? 0;
            _maxX = Output?.Position.X + Output?.Width - _halfDisplayWidth ?? 0;
            _minY = Output?.Position.Y - _halfDisplayHeight ?? 0;
            _maxY = Output?.Position.Y + Output?.Height - _halfDisplayHeight ?? 0;
        }

        private float[] _rotationMatrix;
        private float _halfDisplayWidth, _halfDisplayHeight, _halfTabletWidth, _halfTabletHeight;
        private float _minX, _maxX, _minY, _maxY;

        public struct InkReport
        {
            public Byte vmultiId;
            public Byte reportLength;
            public Byte reportId;
            public Byte buttons;
            public ushort X;
            public ushort Y;
            public ushort pressure;
        }

        public byte[] getBytes(InkReport str)
        {
            int size = Marshal.SizeOf(str);
            byte[] arr = new byte[size];

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(str, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        public void GetVirtualDevice()
        {
            // Connect to HID device from Vmulti
            var matching = Devices.Where(d => d.GetMaxOutputReportLength() == 65 & d.GetMaxInputReportLength() == 65);
            var TabletDevice = matching.FirstOrDefault(d => d.ProductID == 47820);

            if (TabletDevice != null)
            {
                isOpen = Open(TabletDevice);
            }
            else
            {
                Log.Write("WindowsInk", "Failed to find VirtualTablet. Make sure you have the drivers installed", true);
            }
        }

        internal bool Open(HidDevice device)
        {
            VirtualTablet = device;
            if (VirtualTablet != null)
            {
                var config = new OpenConfiguration();
                config.SetOption(OpenOption.Priority, OpenPriority.Low);
                if (VirtualTablet.TryOpen(config, out var stream, out var exception))
                {
                    ReportStream = (HidStream)stream;
                }
                if (ReportStream == null)
                {
                    Log.Write("Detect", "Failed to open VirtualTablet. Make sure you have required permissions to open device streams.", true);
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        public void Position(ITabletReport report)
        {
            if (TabletProperties.ActiveReportID != 0 && report.ReportID <= TabletProperties.ActiveReportID)
                return;

            var pos = new Point(report.Position.X, report.Position.Y);

            // Pre Filter
            foreach (IFilter filter in _preFilters)
                pos = filter.Filter(pos);

            // Normalize (ratio of 1)
            pos.X /= TabletProperties.MaxX;
            pos.Y /= TabletProperties.MaxY;

            // Scale to tablet dimensions (mm)
            pos.X *= TabletProperties.Width;
            pos.Y *= TabletProperties.Height;

            // Adjust area to set origin to 0,0
            pos -= Input.Position;

            // Rotation
            if (Input.Rotation != 0f)
            {
                var tempCopy = new Point(pos.X, pos.Y);
                pos.X = (tempCopy.X * _rotationMatrix[0]) + (tempCopy.Y * _rotationMatrix[1]);
                pos.Y = (tempCopy.X * _rotationMatrix[2]) + (tempCopy.Y * _rotationMatrix[3]);
            }

            // Move area back
            pos.X += _halfTabletWidth;
            pos.Y += _halfTabletHeight;

            // Scale to tablet area (ratio of 1)
            pos.X /= Input.Width;
            pos.Y /= Input.Height;

            // Scale to display area
            pos.X *= Output.Width;
            pos.Y *= Output.Height;

            // Adjust display offset by center
            pos.X += Output.Position.X - _halfDisplayWidth;
            pos.Y += Output.Position.Y - _halfDisplayHeight;

            // Clipping to display bounds
            if (AreaClipping)
            {
                if (pos.X < _minX)
                    pos.X = _minX;
                if (pos.X > _maxX)
                    pos.X = _maxX;
                if (pos.Y < _minY)
                    pos.Y = _minY;
                if (pos.Y > _maxY)
                    pos.Y = _maxY;
            }

            // Post Filter
            foreach (IFilter filter in _postFilters)
                pos = filter.Filter(pos);

            // Setting report position based on Logical Maximum
            double offsetX = -(32767.0 / Output.Width);
            double offsetY = -(32767.0 / Output.Height);

            var pos_x = Math.Round(pos.X / _selectedDisplay.Width * 32767.0 + offsetX);
            var pos_y = Math.Round(pos.Y / _selectedDisplay.Height * 32767.0 + offsetY);

            // Clipping to logical bounds
            if (pos_x < 0)
                pos_x = 0;
            if (pos_x > 32767.0)
                pos_x = 32767;
            if (pos_y < 0)
                pos_y = 0;
            if (pos_y > 32767.0)
                pos_y = 32767;

            // Create virtual report
            double normpressure = (double)report.Pressure / (double)TabletProperties.MaxPressure;
            double pressure = Math.Round(normpressure * 8191.0);
            int tipState = 0;
            tipState |= 1 << 4;
            // bit position - function
            // 0 - press
            // 1 - barrel
            // 2 - eraser
            // 3 - Invert
            // 4 - range
            var erasing = false;
            // we need to send an out of range report when switching to and from eraser mode
            // this should be implemented in the binding process

            if (erasing)
            {
                tipState |= 1 << 3; 
            }

            if (pressure != 0)
            {
                tipState = erasing ? tipState | (1 << 0) : tipState | (1 << 2);
            }

            var virtualReport = new InkReport
            {
                vmultiId = 0x40,
                reportLength = 0XA,
                reportId = 0X5,
                buttons = (byte)tipState,
                X = (ushort)pos_x,
                Y = (ushort)pos_y,
                pressure = (ushort)pressure
            };

            try
            {
                ReportStream.Write(getBytes(virtualReport));
            }
            catch (Exception e)
            {
                Log.Write("WindowsInk", e.ToString(), true);
            }
        }
    }
}