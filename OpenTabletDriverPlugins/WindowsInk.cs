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
using System.Runtime.CompilerServices;

namespace TabletDriverPlugins
{
    [PluginName("Windows Ink")]
    public class AbsoluteInk : IBindingHandler<IBinding>, IAbsoluteMode
    {
        private IEnumerable<HidDevice> Devices => DeviceList.Local.GetHidDevices();
        private HidDevice VirtualTablet { set; get; }

        private Boolean isOpen = false;

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
        public IVirtualScreen _virtualScreen;
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

        public IVirtualScreen VirtualScreen
        {
            set
            {
                _virtualScreen = value;
            }
            get => _virtualScreen;
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

        // HID report structure for XP-Pen drivers. They are used to send windows ink information to windows
        private struct InkReport
        {
            public Byte vmultiId;       // ID to communicate to the Digitizer device
            public Byte reportLength;   // Size of the report in bytes.
            public Byte reportId;       // ID of the report. not 100% sure. i think it to report if its active or idle
            public Byte buttons;        // Byte with switches for pen buttons / states
            public ushort X;            // X position of the pen from 0 to 32767
            public ushort Y;            // Y position of the pen from 0 to 32767
            public ushort pressure;     // Pressure level from 0 to 8191
        }

        private byte[] GetBytes(InkReport str)
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
            // Connect to HID device from XP-Pen drivers
            var matching = Devices.Where(d => d.GetMaxOutputReportLength() == 65 & d.GetMaxInputReportLength() == 65);
            var tabletDevice = matching.FirstOrDefault(d => d.ProductID == 47820);

            if (tabletDevice != null)
            {
                isOpen = Open(tabletDevice);
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
                if (VirtualTablet.TryOpen(config, out var stream, out _))
                {
                    ReportStream = (HidStream)stream;
                }
                if (ReportStream == null)
                {
                    Log.Write("WindowsInk", "Failed to open VirtualTablet. Make sure you have required permissions to open device streams.", true);
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }


        private static BindingHandler _InkBindingHandler;

        public static BindingHandler InkBindingHandler
        {
            get
            {
                if (_InkBindingHandler == null)
                {
                    _InkBindingHandler = new BindingHandler();
                }
                return _InkBindingHandler;
            }
        }
        public class BindingHandler
        {
            private void KeyPress(string key, bool isPress)
            {
                PenKeys[key] = isPress;
                PenStatus = "Lift-Out";
            }

            public void KeyToggle(string key, bool isPressed)
            {
                
                var newkey = key.Replace("Toggle", "");
                if (isPressed & !PenKeys[key] )
                {
                    PenKeys[newkey] = !PenKeys[newkey];
                    PenStatus = "Lift-Out";
                }
                PenKeys[key] = isPressed;
            }
            public void Press(string key)
            {
                if (Supportedkeys.Contains(key))
                {
                    Log.Write("WindowsInk", "Unsuported key pressed");
                    return;
                }
                if (key.Contains("Toggle"))
                {
                    KeyToggle(key, true);
                }
                else
                {
                    KeyPress(key, true);
                }
            }
            public void Release(string key)
            {
                if (Supportedkeys.Contains(key))
                {
                    Log.Write("WindowsInk", "Unsuported key pressed");
                    return;
                }
                if (key.Contains("Toggle"))
                {
                    KeyToggle(key, false);
                }
                else
                {
                    KeyPress(key, false);
                }
            }

            public Dictionary<string, bool> PenKeys = new Dictionary<string, bool>
            {
                {"Eraser", false},
                {"EraserToggle", false},
                {"Barrel", false},
                {"BarrelToggle", false},
            };

            public string PenStatus = "";

            public List<string> Supportedkeys = new List<string>
            {
                {"Eraser"},
                {"EraserToggle"},
                {"Barrel"},
                {"BarrelToggle"},
            };
        }

        // Not really used, WindowsInk expects a touch on pressure > 0. Need to keep it because we inherit IAbsoluteMode
        public float TipActivationPressure { set; get; } 
        public IBinding TipBinding { set; get; } = null;
        public Dictionary<int, IBinding> PenButtonBindings { set; get; } = new Dictionary<int, IBinding>();
        public Dictionary<int, IBinding> AuxButtonBindings { set; get; } = new Dictionary<int, IBinding>();

        private IList<bool> PenButtonStates = new bool[2];
        private IList<bool> AuxButtonStates = new bool[6];

        public void HandleBinding(IDeviceReport report)
        {
            if (report is ITabletReport tabletReport && tabletReport.ReportID >= TabletProperties.ActiveReportID)
                HandlePenBinding(tabletReport);
            if (report is IAuxReport auxReport)
                HandleAuxBinding(auxReport);
        }

        private void HandlePenBinding(ITabletReport report)
        {
            for (var penButton = 0; penButton < 2; penButton++)
            {
                if (PenButtonBindings.TryGetValue(penButton, out var binding) && binding != null)
                {
                    if (report.PenButtons[penButton] && !PenButtonStates[penButton])
                        binding.Press();
                    else if (!report.PenButtons[penButton] && PenButtonStates[penButton])
                        binding.Release();
                }
                PenButtonStates[penButton] = report.PenButtons[penButton];
            }
        }

        private void HandleAuxBinding(IAuxReport report)
        {
            for (var auxButton = 0; auxButton < 6; auxButton++)
            {
                if (AuxButtonBindings.TryGetValue(auxButton, out var binding) && binding != null)
                {
                    if (report.AuxButtons[auxButton] && !AuxButtonStates[auxButton])
                        binding.Press();
                    else if (!report.AuxButtons[auxButton] && AuxButtonStates[auxButton])
                        binding.Release();
                }
                AuxButtonStates[auxButton] = report.AuxButtons[auxButton];
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

            // Move from pixel positioning to windows ink positioning (0 to 32767)
            var pos_x = Math.Round(pos.X / _virtualScreen.Width * 32767.0 + offsetX);
            var pos_y = Math.Round(pos.Y / _virtualScreen.Height * 32767.0 + offsetY);

            // Clipping to logical bounds
            if (pos_x < 0)
                pos_x = 0;
            if (pos_x > 32767.0)
                pos_x = 32767;
            if (pos_y < 0)
                pos_y = 0;
            if (pos_y > 32767.0)
                pos_y = 32767;

            // Normalize pressure
            double normpressure = (double)report.Pressure / (double)TabletProperties.MaxPressure;
            // Expand pressure to fit driver supported pressure levels
            double pressure = Math.Round(normpressure * 8191.0);
            // bit position - function
            // 0 - press
            // 1 - barrel
            // 2 - eraser
            // 3 - Invert
            // 4 - range

            Dictionary<string, int> bitPos = new Dictionary<string, int>
            {
                {"Press", 0},
                {"Barrel", 1},
                {"Eraser", 2},
                {"Invert", 3},
                {"InRange", 4},
            };

            int tipState = 0;
            // We need to send an out of range report when switching to and from eraser mode. 
            // To satisfy windows completely we will lift the pen, exit ranhe, hover again, and then touch.
            // https://docs.microsoft.com/en-us/windows-hardware/design/component-guidelines/windows-pen-states
            switch (InkBindingHandler.PenStatus)
            {
                case "Lift-Out":
                    pressure = 0;
                    tipState |= 1 << bitPos["InRange"];
                    InkBindingHandler.PenStatus = "OutRange";
                    break;
                case "OutRange":
                    pressure = 0;
                    InkBindingHandler.PenStatus = "Lift-In";
                    break;
                case "Lift-In":
                    pressure = 0;
                    tipState |= 1 << bitPos["InRange"];
                    InkBindingHandler.PenStatus = "";
                    break;
                default:
                    tipState |= 1 << bitPos["InRange"];
                    break;
            }

            // Setting button bit switches
            if (InkBindingHandler.PenKeys["Eraser"])
            {
                tipState |= 1 << bitPos["Invert"]; 
            }

            if (InkBindingHandler.PenKeys["Barrel"])
            {
                tipState |= 1 << bitPos["Barrel"];
            }

            if (pressure != 0)
            {
                if (InkBindingHandler.PenKeys["Eraser"])
                {
                    tipState |= 1 << bitPos["Eraser"];
                }
                else
                {
                    tipState |= 1 << bitPos["Press"];

                }
            }

            // Setting the report values
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
                ReportStream.Write(GetBytes(virtualReport));
            }
            catch (Exception e)
            {
                Log.Write("WindowsInk", e.ToString(), true);
            }
        }
    }

    [PluginName("InkBinding")]
    public class InkBinding : IBinding
    {
        public string Name
        {
            get
            {
                return nameof(InkBinding) + ": " + Property;
            }
        }

        public string Property { set; get; }

        public Action Press
        {
            get
            {
                AbsoluteInk.BindingHandler PenHandler = AbsoluteInk.InkBindingHandler;
                return () => PenHandler.Press(Property);
            }
        }

        public Action Release
        {
            get
            {
                AbsoluteInk.BindingHandler PenHandler = AbsoluteInk.InkBindingHandler;
                return () => PenHandler.Release(Property);
            }
        }

        public override string ToString() => Name;
    }
}